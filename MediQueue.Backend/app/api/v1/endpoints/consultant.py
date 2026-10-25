"""Routes du consultant : toutes protégées par jeton, et limitées au centre et au service du consultant."""
from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.api.deps import get_current_consultant, get_db
from app.models import Consultant
from app.schemas import CallNextRequest, CallNextResponse, DashboardResponse
from app.services import ticket_service

router = APIRouter(prefix="/consultant", tags=["Consultant"])


@router.get("/dashboard", response_model=DashboardResponse, summary="File d'attente de mon service (lecture seule)")
def dashboard(consultant: Consultant = Depends(get_current_consultant), db: Session = Depends(get_db)) -> DashboardResponse:
    return ticket_service.dashboard(db, consultant)


@router.post("/queue/call-next", response_model=CallNextResponse,
             summary="Appeler le patient suivant (seule route qui fait avancer la file)")
def call_next(body: CallNextRequest, consultant: Consultant = Depends(get_current_consultant),
              db: Session = Depends(get_db)) -> CallNextResponse:
    return ticket_service.call_next(db, consultant, body.expected_number)
