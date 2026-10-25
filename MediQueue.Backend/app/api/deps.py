"""Dépendances FastAPI : session de base de données, réglages, consultant authentifié, limiteurs."""
from __future__ import annotations

from collections.abc import Iterator

from fastapi import Depends, Request
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy.orm import Session

from app.core.config import Settings
from app.core.errors import unauthorized
from app.models import Consultant
from app.services import auth_service

_bearer = HTTPBearer(auto_error=False)


def get_settings(request: Request) -> Settings:
    return request.app.state.settings


def get_db(request: Request) -> Iterator[Session]:
    session = request.app.state.session_factory()
    try:
        yield session
    finally:
        session.close()


def get_current_consultant(
    credentials: HTTPAuthorizationCredentials | None = Depends(_bearer),
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> Consultant:
    if credentials is None or credentials.scheme.lower() != "bearer":
        raise unauthorized("Authentification requise.", "AUTH_REQUIRED")
    return auth_service.authenticate(db, settings, credentials.credentials)


def client_ip(request: Request) -> str:
    return request.client.host if request.client else "inconnu"
