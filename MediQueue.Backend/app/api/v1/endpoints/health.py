from fastapi import APIRouter, Request

from app import API_VERSION
from app.schemas import HealthResponse

router = APIRouter()


@router.get("/health", response_model=HealthResponse, summary="Le serveur répond")
def health(request: Request) -> HealthResponse:
    publisher = getattr(request.app.state, "publisher", None)
    return HealthResponse(status="ok", api_version=API_VERSION, discovery=getattr(publisher, "status", "disabled"))
