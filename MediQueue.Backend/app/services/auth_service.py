"""Authentification des consultants : identifiants → jeton ; jeton → consultant actif."""
from __future__ import annotations

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.core.config import Settings
from app.core.errors import forbidden, unauthorized
from app.core.security import (
    TokenError, create_access_token, decode_access_token, verify_password,
)
from app.models import Center, Consultant, Service
from app.schemas import LoginResponse

GENERIC_LOGIN_ERROR = "Identifiants incorrects."


def login(db: Session, settings: Settings, center_code: str, consultant_code: str, password: str) -> LoginResponse:
    row = db.execute(
        select(Consultant, Center, Service)
        .join(Center, Center.id == Consultant.center_id)
        .join(Service, (Service.id == Consultant.service_id) & (Service.center_id == Consultant.center_id))
        .where(func.upper(Center.code) == center_code.strip().upper(),
               func.upper(Consultant.login_code) == consultant_code.strip().upper())
    ).first()

    # On vérifie TOUJOURS un hachage (factice si inconnu) : même durée, même message, quelle que soit la cause.
    password_ok = verify_password(row.Consultant.password_hash if row else None, password)
    if row is None or not password_ok:
        raise unauthorized(GENERIC_LOGIN_ERROR, "INVALID_CREDENTIALS")
    if not (row.Consultant.is_active and row.Center.is_active and row.Service.is_active):
        raise forbidden("Ce compte est désactivé. Contactez l'administrateur du centre.", "ACCOUNT_DISABLED")

    token, ttl = create_access_token(settings, consultant_id=row.Consultant.id,
                                     center_id=row.Center.id, service_id=row.Service.id)
    return LoginResponse(access_token=token, expires_in=ttl, center_name=row.Center.name, service_name=row.Service.name)


def authenticate(db: Session, settings: Settings, token: str) -> Consultant:
    """Le jeton prouve l'identité, mais l'état du compte est TOUJOURS relu en base :
    désactiver un consultant ou le changer de service prend effet immédiatement."""
    try:
        claims = decode_access_token(settings, token)
    except TokenError:
        raise unauthorized("Session expirée ou invalide. Reconnectez-vous.", "INVALID_TOKEN") from None

    row = db.execute(
        select(Consultant, Center, Service)
        .join(Center, Center.id == Consultant.center_id)
        .join(Service, (Service.id == Consultant.service_id) & (Service.center_id == Consultant.center_id))
        .where(Consultant.id == claims.consultant_id)
    ).first()
    if (row is None or row.Consultant.center_id != claims.center_id or row.Consultant.service_id != claims.service_id
            or not (row.Consultant.is_active and row.Center.is_active and row.Service.is_active)):
        raise unauthorized("Session expirée ou invalide. Reconnectez-vous.", "INVALID_TOKEN")
    return row.Consultant
