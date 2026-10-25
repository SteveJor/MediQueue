"""Fabrique de l'application FastAPI."""
from __future__ import annotations

import time
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from sqlalchemy import text

from app import API_VERSION, __version__
from app.api.v1.endpoints import health as health_endpoint
from app.api.v1.router import api_router
from app.core.config import ConfigurationError, Settings
from app.core.errors import install_error_handlers
from app.core.logging import configure_logging, get_logger
from app.core.throttle import LoginThrottle, SlidingWindowLimiter
from app.db.session import make_engine, make_session_factory

log = get_logger("http")


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or Settings()
    configure_logging()

    engine = make_engine(settings.database_url)
    session_factory = make_session_factory(engine)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        # Base absente ou non migrée : on s'arrête AVANT d'ouvrir le service, avec un message actionnable.
        try:
            with engine.connect() as conn:
                conn.execute(text("SELECT 1 FROM tickets LIMIT 1"))
        except Exception as exc:
            raise ConfigurationError(
                "Base de données absente ou non initialisée. Lancez : python -m app.cli init-db"
            ) from exc
        log.info("MediQueue %s démarré (API v%s, TLS=%s)", __version__, API_VERSION, settings.tls_enabled)
        yield
        engine.dispose()

    app = FastAPI(
        title="MediQueue API", version=__version__, lifespan=lifespan,
        docs_url="/docs" if settings.docs_enabled else None,
        redoc_url=None, openapi_url="/openapi.json" if settings.docs_enabled else None,
        description="Gestion des files d'attente en centre de santé. La file n'avance que par l'appel confirmé du consultant.",
    )
    app.state.settings = settings
    app.state.engine = engine
    app.state.session_factory = session_factory
    app.state.publisher = None   # renseigné par app.run lorsque la découverte mDNS est active
    app.state.ticket_limiter = SlidingWindowLimiter(settings.ticket_rate_per_minute, 60.0)
    app.state.login_throttle = LoginThrottle(settings.login_max_failures, settings.login_lockout_seconds)

    install_error_handlers(app)

    @app.middleware("http")
    async def _log_and_harden(request: Request, call_next):
        started = time.perf_counter()
        response = await call_next(request)
        route = request.scope.get("route")
        # On journalise le MODÈLE de route (pas le chemin réel : il contient l'identifiant du ticket, qui est une clé d'accès).
        log.info("%s %s -> %s (%.0f ms)", request.method, getattr(route, "path", "(inconnue)"), response.status_code,
                 (time.perf_counter() - started) * 1000)
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        return response

    app.include_router(api_router)
    app.add_api_route("/health", health_endpoint.health, methods=["GET"], include_in_schema=False)  # contrat : GET /health
    return app
