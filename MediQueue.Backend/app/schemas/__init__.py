"""Schémas Pydantic : contrat JSON en snake_case, identique à celui attendu par les applications MAUI."""
from __future__ import annotations

import re
from typing import Annotated

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, field_validator

from app.models import TicketStatus

QrToken = Annotated[str, StringConstraints(pattern=r"^[A-Za-z0-9_-]{6,64}$")]
_CONTROL_CHARS = re.compile(r"[\x00-\x1f\x7f]")


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


class QrValidateRequest(_Strict):
    qr_token: QrToken


class QrTargetResponse(BaseModel):
    qr_token: str
    center_id: int
    center_name: str
    service_id: int
    service_name: str


class TicketCreateRequest(_Strict):
    qr_token: QrToken
    patient_name: str | None = None

    @field_validator("patient_name")
    @classmethod
    def _clean_name(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            return None                      # nom vide = pas de nom (champ facultatif)
        if len(value) > 40:
            raise ValueError("40 caractères maximum")
        if _CONTROL_CHARS.search(value):
            raise ValueError("caractères de contrôle interdits")
        return value


class TicketResponse(BaseModel):
    id: str
    number: int
    center_name: str
    service_name: str
    patient_name: str | None
    status: TicketStatus
    people_ahead: int
    position: int
    ahead_numbers: list[int]


class LoginRequest(_Strict):
    center_code: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=32)]
    consultant_code: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=32)]
    password: Annotated[str, Field(min_length=1, max_length=200)]


class LoginResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"
    expires_in: int
    center_name: str
    service_name: str


class DashboardResponse(BaseModel):
    center_name: str
    service_name: str
    current_number: int | None
    waiting_numbers: list[int]


class CallNextRequest(_Strict):
    expected_number: Annotated[int, Field(ge=1)]


class CallNextResponse(BaseModel):
    called_number: int
    dashboard: DashboardResponse


class HealthResponse(BaseModel):
    status: str
    service: str = "mediqueue"
    api_version: int
    discovery: str
