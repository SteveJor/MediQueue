"""Règles métier de la file d'attente. TOUT ce qui modifie un ticket passe par ici.

Règle fondamentale : seul le consultant fait avancer la file (call_next). Aucun minuteur, aucune tâche planifiée.
Les positions ne sont jamais stockées : elles se déduisent des tickets WAITING du même service à chaque lecture.
"""
from __future__ import annotations

import secrets
from datetime import datetime, timezone

from sqlalchemy import func, select, update
from sqlalchemy.orm import Session

from app.core.errors import AppError, conflict, not_found
from app.db.session import begin_write, lock_service
from app.models import Center, Consultant, QRCode, Service, Ticket, TicketStatus
from app.schemas import (
    CallNextResponse, DashboardResponse, QrTargetResponse, TicketResponse,
)


def _now() -> datetime:
    return datetime.now(timezone.utc)


# ───────────── Patient ─────────────

def _active_qr(db: Session, token: str) -> tuple[QRCode, Center, Service]:
    row = db.execute(
        select(QRCode, Center, Service)
        .join(Service, (Service.id == QRCode.service_id) & (Service.center_id == QRCode.center_id))
        .join(Center, Center.id == QRCode.center_id)
        .where(QRCode.token == token)
    ).first()
    # Même message pour « inconnu » et « désactivé » : on ne révèle pas quels jetons existent.
    if row is None or not (row.QRCode.is_active and row.Center.is_active and row.Service.is_active):
        raise not_found("Ce QR code n'est pas reconnu par MediQueue.", "QR_UNKNOWN")
    return row.QRCode, row.Center, row.Service


def validate_qr(db: Session, token: str) -> QrTargetResponse:
    qr, center, service = _active_qr(db, token)
    return QrTargetResponse(qr_token=qr.token, center_id=center.id, center_name=center.name,
                            service_id=service.id, service_name=service.name)


def create_ticket(db: Session, token: str, patient_name: str | None) -> TicketResponse:
    _active_qr(db, token)                            # rejet rapide d'un QR inconnu, sans prendre le verrou d'écriture
    begin_write(db)                                  # verrou d'écriture AVANT de lire le dernier numéro
    qr, center, service = _active_qr(db, token)      # revérifié sous verrou (le QR a pu être désactivé entre-temps)
    lock_service(db, service.id)
    last = db.execute(select(func.max(Ticket.number)).where(Ticket.service_id == service.id)).scalar() or 0
    ticket = Ticket(id=secrets.token_hex(16), center_id=center.id, service_id=service.id, number=last + 1,
                    patient_name=patient_name, status=TicketStatus.WAITING.value)
    db.add(ticket)
    db.flush()
    view = _ticket_view(db, ticket, center, service)
    db.commit()
    return view


def get_ticket(db: Session, ticket_id: str) -> TicketResponse:
    ticket, center, service = _load_ticket(db, ticket_id)
    return _ticket_view(db, ticket, center, service)


def cancel_ticket(db: Session, ticket_id: str) -> TicketResponse:
    """Le patient quitte la file. Sans effet (renvoie l'état courant) si le ticket n'est plus en attente."""
    _load_ticket(db, ticket_id)                       # 404 rapide, sans verrou, si l'identifiant est inconnu
    begin_write(db)
    ticket, center, service = _load_ticket(db, ticket_id)
    if ticket.status == TicketStatus.WAITING.value:
        ticket.status = TicketStatus.CANCELLED.value
        ticket.cancelled_at = _now()
    view = _ticket_view(db, ticket, center, service)
    db.commit()
    return view


def _load_ticket(db: Session, ticket_id: str) -> tuple[Ticket, Center, Service]:
    if len(ticket_id) != 32:
        raise not_found("Ce ticket n'existe plus.", "TICKET_UNKNOWN")
    row = db.execute(
        select(Ticket, Center, Service)
        .join(Service, (Service.id == Ticket.service_id) & (Service.center_id == Ticket.center_id))
        .join(Center, Center.id == Ticket.center_id)
        .where(Ticket.id == ticket_id)
    ).first()
    if row is None:
        raise not_found("Ce ticket n'existe plus.", "TICKET_UNKNOWN")
    return row.Ticket, row.Center, row.Service


def _ticket_view(db: Session, ticket: Ticket, center: Center, service: Service) -> TicketResponse:
    ahead: list[int] = []
    if ticket.status == TicketStatus.WAITING.value:
        ahead = list(db.execute(
            select(Ticket.number)
            .where(Ticket.service_id == ticket.service_id, Ticket.status == TicketStatus.WAITING.value,
                   Ticket.number < ticket.number)
            .order_by(Ticket.number)
        ).scalars())
    waiting = ticket.status == TicketStatus.WAITING.value
    return TicketResponse(
        id=ticket.id, number=ticket.number, center_name=center.name, service_name=service.name,
        patient_name=ticket.patient_name, status=TicketStatus(ticket.status),
        people_ahead=len(ahead), position=len(ahead) + 1 if waiting else 0, ahead_numbers=ahead,
    )


# ───────────── Consultant ─────────────

def dashboard(db: Session, consultant: Consultant) -> DashboardResponse:
    center = db.get(Center, consultant.center_id)
    service = db.get(Service, consultant.service_id)
    current = db.execute(
        select(Ticket.number).where(Ticket.service_id == consultant.service_id,
                                    Ticket.status == TicketStatus.IN_CONSULTATION.value)
    ).scalar()
    waiting = list(db.execute(
        select(Ticket.number).where(Ticket.service_id == consultant.service_id,
                                    Ticket.status == TicketStatus.WAITING.value).order_by(Ticket.number)
    ).scalars())
    return DashboardResponse(center_name=center.name, service_name=service.name,
                             current_number=current, waiting_numbers=waiting)


def call_next(db: Session, consultant: Consultant, expected_number: int) -> CallNextResponse:
    """Appel du patient suivant : l'UNIQUE opération qui fait avancer la file. Atomique (voir db/session.py).

    1. aucun ticket en attente           → 404 (rien ne change)
    2. le plus ancien n'est pas expected → 409 (rien ne change : le consultant a confirmé un autre patient)
    3. sinon : le patient en consultation passe COMPLETED, le suivant passe IN_CONSULTATION.
    """
    begin_write(db)
    lock_service(db, consultant.service_id)
    next_ticket = db.execute(
        select(Ticket).where(Ticket.service_id == consultant.service_id,
                             Ticket.status == TicketStatus.WAITING.value).order_by(Ticket.number).limit(1)
    ).scalar_one_or_none()
    if next_ticket is None:
        db.rollback()
        raise not_found("Aucun patient en attente.", "QUEUE_EMPTY")
    if next_ticket.number != expected_number:
        db.rollback()
        raise conflict("La file a changé : vérifiez le prochain patient avant d'appeler.", "QUEUE_CHANGED")

    now = _now()
    db.execute(
        update(Ticket)
        .where(Ticket.service_id == consultant.service_id, Ticket.status == TicketStatus.IN_CONSULTATION.value)
        .values(status=TicketStatus.COMPLETED.value, completed_at=now)
    )
    db.flush()   # libère l'index « un seul en consultation » avant d'en promouvoir un autre
    claimed = db.execute(
        update(Ticket)
        .where(Ticket.id == next_ticket.id, Ticket.status == TicketStatus.WAITING.value)
        .values(status=TicketStatus.IN_CONSULTATION.value, called_at=now)
    )
    if claimed.rowcount != 1:                        # filet de sécurité : ne devrait jamais arriver sous verrou
        db.rollback()
        raise conflict("La file a changé : vérifiez le prochain patient avant d'appeler.", "QUEUE_CHANGED")
    db.commit()
    return CallNextResponse(called_number=next_ticket.number, dashboard=dashboard(db, consultant))


__all__ = ["AppError", "validate_qr", "create_ticket", "get_ticket", "cancel_ticket", "dashboard", "call_next"]
