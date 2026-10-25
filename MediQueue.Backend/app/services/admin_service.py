"""Opérations d'administration (CLI uniquement : aucune route HTTP d'administration n'est exposée)."""
from __future__ import annotations

import secrets

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.core.security import hash_password
from app.models import Center, Consultant, QRCode, Service

MIN_PASSWORD_LENGTH = 10


class AdminError(ValueError):
    pass


def create_center(db: Session, code: str, name: str, info: str | None = None) -> Center:
    code = code.strip().upper()
    if db.execute(select(Center).where(func.upper(Center.code) == code)).scalar_one_or_none():
        raise AdminError(f"Le centre {code} existe déjà.")
    center = Center(code=code, name=name.strip(), info=info)
    db.add(center)
    db.flush()
    return center


def get_center(db: Session, code: str) -> Center:
    center = db.execute(select(Center).where(func.upper(Center.code) == code.strip().upper())).scalar_one_or_none()
    if center is None:
        raise AdminError(f"Centre inconnu : {code}.")
    return center


def create_service(db: Session, center_code: str, name: str) -> Service:
    center = get_center(db, center_code)
    if db.execute(select(Service).where(Service.center_id == center.id, Service.name == name.strip())).scalar_one_or_none():
        raise AdminError(f"Le service « {name} » existe déjà dans {center.code}.")
    service = Service(center_id=center.id, name=name.strip())
    db.add(service)
    db.flush()
    return service


def get_service(db: Session, center_code: str, name: str) -> tuple[Center, Service]:
    center = get_center(db, center_code)
    service = db.execute(select(Service).where(Service.center_id == center.id, Service.name == name.strip())).scalar_one_or_none()
    if service is None:
        raise AdminError(f"Service inconnu dans {center.code} : {name}.")
    return center, service


def create_qr(db: Session, center_code: str, service_name: str, token: str | None = None) -> QRCode:
    center, service = get_service(db, center_code, service_name)
    token = token or secrets.token_urlsafe(12)       # 96 bits aléatoires, alphabet URL-safe
    if db.execute(select(QRCode).where(QRCode.token == token)).scalar_one_or_none():
        raise AdminError("Ce jeton de QR existe déjà.")
    qr = QRCode(token=token, center_id=center.id, service_id=service.id)
    db.add(qr)
    db.flush()
    return qr


def _check_password(password: str) -> None:
    if len(password) < MIN_PASSWORD_LENGTH:
        raise AdminError(f"Mot de passe trop court (minimum {MIN_PASSWORD_LENGTH} caractères).")


def create_consultant(db: Session, center_code: str, login_code: str, service_name: str, password: str) -> Consultant:
    _check_password(password)
    center, service = get_service(db, center_code, service_name)
    login_code = login_code.strip().upper()
    if db.execute(select(Consultant).where(Consultant.center_id == center.id,
                                           func.upper(Consultant.login_code) == login_code)).scalar_one_or_none():
        raise AdminError(f"Le consultant {login_code} existe déjà dans {center.code}.")
    consultant = Consultant(center_id=center.id, service_id=service.id, login_code=login_code,
                            password_hash=hash_password(password))
    db.add(consultant)
    db.flush()
    return consultant


def _get_consultant(db: Session, center_code: str, login_code: str) -> Consultant:
    center = get_center(db, center_code)
    consultant = db.execute(select(Consultant).where(
        Consultant.center_id == center.id, func.upper(Consultant.login_code) == login_code.strip().upper()
    )).scalar_one_or_none()
    if consultant is None:
        raise AdminError(f"Consultant inconnu dans {center.code} : {login_code}.")
    return consultant


def set_password(db: Session, center_code: str, login_code: str, password: str) -> None:
    _check_password(password)
    _get_consultant(db, center_code, login_code).password_hash = hash_password(password)


def set_consultant_active(db: Session, center_code: str, login_code: str, active: bool) -> None:
    _get_consultant(db, center_code, login_code).is_active = active
