"""Routes du patient : aucun compte. Un ticket n'est accessible que par son identifiant (128 bits aléatoires)."""
from __future__ import annotations

from fastapi import APIRouter, Depends, Path, Request, status
from sqlalchemy.orm import Session

from app.api.deps import client_ip, get_db
from app.core.errors import too_many
from app.schemas import QrTargetResponse, QrValidateRequest, TicketCreateRequest, TicketResponse
from app.services import ticket_service

router = APIRouter(prefix="/patient", tags=["Patient"])

TicketId = Path(pattern=r"^[0-9a-f]{32}$", description="Identifiant du ticket (32 caractères hexadécimaux)")


@router.post("/qr/validate", response_model=QrTargetResponse, summary="Vérifier un QR code (ne crée rien)")
def validate_qr(body: QrValidateRequest, db: Session = Depends(get_db)) -> QrTargetResponse:
    return ticket_service.validate_qr(db, body.qr_token)


@router.post("/tickets", response_model=TicketResponse, status_code=status.HTTP_201_CREATED,
             summary="Créer un ticket après scan d'un QR code")
def create_ticket(body: TicketCreateRequest, request: Request, db: Session = Depends(get_db)) -> TicketResponse:
    wait = request.app.state.ticket_limiter.hit(client_ip(request))
    if wait:
        raise too_many("Trop de tickets demandés depuis cet appareil. Réessayez dans un instant.", wait)
    return ticket_service.create_ticket(db, body.qr_token, body.patient_name)


@router.get("/tickets/{ticket_id}", response_model=TicketResponse, summary="Lire un ticket et sa position")
def get_ticket(ticket_id: str = TicketId, db: Session = Depends(get_db)) -> TicketResponse:
    return ticket_service.get_ticket(db, ticket_id)


@router.post("/tickets/{ticket_id}/cancel", response_model=TicketResponse,
             summary="Le patient quitte la file (ticket en attente uniquement)")
def cancel_ticket(ticket_id: str = TicketId, db: Session = Depends(get_db)) -> TicketResponse:
    return ticket_service.cancel_ticket(db, ticket_id)
