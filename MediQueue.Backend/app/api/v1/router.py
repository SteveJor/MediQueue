from fastapi import APIRouter

from app.api.v1.endpoints import auth, consultant, health, patient

api_router = APIRouter(prefix="/api/v1")
api_router.include_router(health.router)
api_router.include_router(auth.router)
api_router.include_router(patient.router)
api_router.include_router(consultant.router)
