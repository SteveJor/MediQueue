"""Point d'entrée : `python -m app`.

Ordre obligatoire (cahier des charges, § 6) :
  1. valider la configuration  2. démarrer le serveur HTTPS  3. vérifier qu'il accepte réellement des connexions TLS
  4. PUIS publier le service DNS-SD (avec le port réellement ouvert)  …  arrêt : retirer l'annonce, puis fermer.

FastAPI/uvicorn exécutent le « lifespan » AVANT d'ouvrir le port : y publier annoncerait un service pas encore joignable.
On surcharge donc uvicorn.Server.startup/shutdown, qui sont appelés après l'ouverture et avant la fermeture des sockets.
"""
from __future__ import annotations

import asyncio
import socket
import ssl
import sys
from collections.abc import Callable
from pathlib import Path

import uvicorn

from app.core.config import ConfigurationError, Settings, validate_for_serving
from app.core.logging import configure_logging, get_logger
from app.discovery.network import list_adapters, select_announce_addresses
from app.discovery.publisher import MdnsPublisher, build_txt_properties
from app.main import create_app
from app.tls.pin import certificate_hint

log = get_logger("run")


def probe_listener(port: int, cert_file: Path | None, timeout: float = 5.0) -> None:
    """Se connecte à NOTRE propre port. En TLS : handshake complet, certificat vérifié contre notre propre
    certificat (validation normale, rien n'est désactivé). Lève OSError/ssl.SSLError si le serveur n'est pas prêt."""
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as raw:
        if cert_file is None:
            return
        context = ssl.create_default_context(cafile=str(cert_file))
        with context.wrap_socket(raw, server_hostname="127.0.0.1"):
            return


class MediQueueServer(uvicorn.Server):
    def __init__(self, config: uvicorn.Config, settings: Settings,
                 publisher_factory: Callable[..., MdnsPublisher] = MdnsPublisher):
        super().__init__(config)
        self.settings = settings
        self._publisher_factory = publisher_factory
        self.publisher: MdnsPublisher | None = None
        self.bound_port: int | None = None

    async def startup(self, sockets: list[socket.socket] | None = None) -> None:
        await super().startup(sockets)          # lifespan, puis ouverture des sockets (TLS)
        if self.should_exit:
            return
        self.bound_port = self.servers[0].sockets[0].getsockname()[1]
        log.info("Serveur à l'écoute sur %s:%d (%s)", self.settings.bind_host, self.bound_port,
                 "HTTPS" if self.settings.tls_enabled else "HTTP EN CLAIR — développement uniquement")
        await self._publish()

    async def shutdown(self, sockets: list[socket.socket] | None = None) -> None:
        if self.publisher is not None:
            await self.publisher.stop()          # retirer l'annonce AVANT de fermer le port
        await super().shutdown(sockets)

    async def _publish(self) -> None:
        settings = self.settings
        app = self.config.app
        if not settings.mdns_enabled:
            log.info("Découverte mDNS/DNS-SD désactivée (MEDIQUEUE_MDNS_ENABLED=false).")
            return

        addresses = select_announce_addresses(
            list_adapters(), settings.excluded_adapter_patterns(), settings.announce_addresses_override()
        )
        if not addresses:
            log.error("Aucune adresse IPv4 de réseau local détectée : service NON annoncé. "
                      "Renseignez MEDIQUEUE_MDNS_ADDRESSES ou vérifiez que vous êtes connecté au réseau.")
            return

        loop = asyncio.get_running_loop()
        try:   # le port répond-il vraiment ? (ne jamais annoncer un service injoignable)
            await loop.run_in_executor(
                None, probe_listener, self.bound_port, settings.tls_cert_file if settings.tls_enabled else None)
        except Exception as exc:
            log.error("Le serveur ne répond pas sur son propre port (%s : %s) : service NON annoncé.",
                      type(exc).__name__, exc)
            return

        host = socket.gethostname().split(".")[0]
        hint = certificate_hint(settings.tls_cert_file) if settings.tls_enabled else "0" * 16
        self.publisher = self._publisher_factory(
            service_type=settings.mdns_service_type,
            instance_name=settings.mdns_instance_name or f"MediQueue {host}",
            hostname=settings.mdns_hostname or f"{host}.local.",
            addresses=addresses,
            port=self.bound_port,                # le port RÉEL, lu sur la socket ouverte
            properties=build_txt_properties(pin_hint=hint, tls=settings.tls_enabled, center_hint=settings.center_hint),
        )
        app.state.publisher = self.publisher
        await self.publisher.start()


def build_server(settings: Settings, publisher_factory: Callable[..., MdnsPublisher] = MdnsPublisher) -> MediQueueServer:
    app = create_app(settings)
    config = uvicorn.Config(
        app, host=settings.bind_host, port=settings.port, access_log=False, log_level="info",
        ssl_certfile=str(settings.tls_cert_file) if settings.tls_enabled else None,
        ssl_keyfile=str(settings.tls_key_file) if settings.tls_enabled else None,
        server_header=False,
        timeout_graceful_shutdown=5,     # arrêt borné : une connexion inactive ne bloque pas l'extinction
    )
    return MediQueueServer(config, settings, publisher_factory)


def main() -> int:
    configure_logging()
    try:
        settings = Settings()
        validate_for_serving(settings)
        if settings.allow_insecure_http:
            log.warning("MEDIQUEUE_ALLOW_INSECURE_HTTP=true : HTTP en clair, réservé au développement. "
                        "Les applications MediQueue refusent de s'y connecter.")
        build_server(settings).run()
    except ConfigurationError as exc:
        log.error("Configuration invalide : %s", exc)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
