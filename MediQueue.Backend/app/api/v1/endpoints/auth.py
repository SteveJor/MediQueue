from __future__ import annotations

from fastapi import APIRouter, Depends, Request
from sqlalchemy.orm import Session

from app.api.deps import client_ip, get_db, get_settings
from app.core.config import Settings
from app.core.errors import AppError, too_many
from app.schemas import LoginRequest, LoginResponse
from app.services import auth_service

router = APIRouter(prefix="/auth", tags=["Authentification"])


@router.post("/login", response_model=LoginResponse, summary="Authentification d'un consultant")
def login(body: LoginRequest, request: Request, db: Session = Depends(get_db),
          settings: Settings = Depends(get_settings)) -> LoginResponse:
    throttle = request.app.state.login_throttle
    # Deux clés : l'adresse cliente (balayage de comptes) et le compte visé (essais sur un compte depuis plusieurs adresses).
    keys = (f"ip:{client_ip(request)}", f"acct:{body.center_code.strip().upper()}/{body.consultant_code.strip().upper()}")
    for key in keys:
        wait = throttle.retry_after(key)
        if wait:
            raise too_many("Trop de tentatives de connexion. Réessayez plus tard.", wait)
    try:
        result = auth_service.login(db, settings, body.center_code, body.consultant_code, body.password)
    except AppError as exc:
        if exc.code == "INVALID_CREDENTIALS":
            for key in keys:
                throttle.record_failure(key)
        raise
    for key in keys:
        throttle.reset(key)
    return result
