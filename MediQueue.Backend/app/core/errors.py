"""Erreurs applicatives et enveloppe d'erreur uniforme : {"detail": "message lisible", "code": "CODE"}."""
from __future__ import annotations

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.core.logging import get_logger

log = get_logger("errors")


class AppError(Exception):
    def __init__(self, status_code: int, detail: str, code: str, headers: dict[str, str] | None = None):
        super().__init__(detail)
        self.status_code = status_code
        self.detail = detail
        self.code = code
        self.headers = headers or {}


def not_found(detail: str, code: str = "NOT_FOUND") -> AppError:
    return AppError(404, detail, code)


def conflict(detail: str, code: str = "CONFLICT") -> AppError:
    return AppError(409, detail, code)


def unauthorized(detail: str = "Authentification requise.", code: str = "UNAUTHORIZED") -> AppError:
    return AppError(401, detail, code, {"WWW-Authenticate": "Bearer"})


def forbidden(detail: str = "Accès refusé.", code: str = "FORBIDDEN") -> AppError:
    return AppError(403, detail, code)


def too_many(detail: str, retry_after: int) -> AppError:
    return AppError(429, detail, "TOO_MANY_REQUESTS", {"Retry-After": str(max(1, retry_after))})


def install_error_handlers(app: FastAPI) -> None:
    @app.exception_handler(AppError)
    async def _app_error(_: Request, exc: AppError) -> JSONResponse:
        return JSONResponse({"detail": exc.detail, "code": exc.code}, status_code=exc.status_code, headers=exc.headers)

    @app.exception_handler(RequestValidationError)
    async def _validation(_: Request, exc: RequestValidationError) -> JSONResponse:
        # On ne renvoie JAMAIS la valeur reçue (« input ») : elle peut contenir un mot de passe.
        errors = [{"loc": [str(p) for p in e.get("loc", [])], "msg": e.get("msg", "invalide")} for e in exc.errors()]
        return JSONResponse(
            {"detail": "Données invalides.", "code": "VALIDATION_ERROR", "errors": errors}, status_code=422
        )

    @app.exception_handler(StarletteHTTPException)
    async def _http(_: Request, exc: StarletteHTTPException) -> JSONResponse:
        detail = exc.detail if isinstance(exc.detail, str) else "Requête refusée."
        return JSONResponse({"detail": detail, "code": f"HTTP_{exc.status_code}"}, status_code=exc.status_code,
                            headers=getattr(exc, "headers", None))

    @app.exception_handler(Exception)
    async def _unexpected(request: Request, exc: Exception) -> JSONResponse:
        # Détails internes dans les journaux seulement (jamais dans la réponse, jamais le corps de la requête).
        log.error("Erreur interne sur %s %s : %s", request.method, request.url.path.split("/")[1:3], type(exc).__name__,
                  exc_info=exc)
        return JSONResponse({"detail": "Erreur interne du serveur.", "code": "INTERNAL_ERROR"}, status_code=500)
