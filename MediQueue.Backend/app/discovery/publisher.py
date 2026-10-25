"""Publication DNS-SD (mDNS) du service MediQueue avec python-zeroconf.

DNS-SD ne prouve RIEN sur l'identité du serveur : n'importe quel appareil peut annoncer un faux service MediQueue.
L'authentification du serveur est faite par les clients via TLS + empreinte de clé (voir app/tls/pin.py).
Les métadonnées TXT sont publiques et minimales : aucune donnée personnelle, aucun jeton, aucun secret.
"""
from __future__ import annotations

import asyncio
import socket
from dataclasses import dataclass, field

from zeroconf import ServiceInfo
from zeroconf.asyncio import AsyncZeroconf

from app.core.logging import get_logger

log = get_logger("mdns")


def build_txt_properties(*, pin_hint: str, tls: bool, center_hint: str = "") -> dict[str, str]:
    props = {"v": "1", "path": "/api/v1", "tls": "1" if tls else "0", "fp": pin_hint}
    if center_hint:
        props["c"] = center_hint
    return props


@dataclass
class MdnsPublisher:
    service_type: str                 # ex. _mediqueue._tcp.local.
    instance_name: str                # ex. « MediQueue SM-001 »
    hostname: str                     # ex. mediqueue-pc.local.
    addresses: list[str]
    port: int
    properties: dict[str, str]
    status: str = "disabled"          # disabled | published | failed
    registered_name: str = ""
    _aiozc: AsyncZeroconf | None = field(default=None, repr=False)
    _info: ServiceInfo | None = field(default=None, repr=False)

    def build_info(self) -> ServiceInfo:
        return ServiceInfo(
            type_=self.service_type,
            name=f"{self.instance_name}.{self.service_type}",
            addresses=[socket.inet_aton(a) for a in self.addresses],
            port=self.port,
            properties=self.properties,
            server=self.hostname,
        )

    async def start(self, timeout: float = 15.0) -> None:
        try:
            # On restreint zeroconf aux interfaces choisies : pas d'annonce sur les cartes virtuelles ou de VPN.
            self._aiozc = AsyncZeroconf(interfaces=self.addresses)
            self._info = self.build_info()
            task = await self._aiozc.async_register_service(self._info, allow_name_change=True)
            await asyncio.wait_for(task, timeout=timeout)
            self.registered_name = self._info.name
            self.status = "published"
            log.info("Service annoncé : %s, port %d, adresses %s", self.registered_name, self.port,
                     ", ".join(self.addresses))
        except Exception as exc:       # l'API continue de fonctionner ; l'échec est visible dans /health et les journaux
            self.status = "failed"
            log.error("Échec de la publication mDNS/DNS-SD : %s : %s", type(exc).__name__, exc)
            await self._close()

    async def stop(self) -> None:
        if self._aiozc is not None and self._info is not None and self.status == "published":
            try:
                await (await self._aiozc.async_unregister_service(self._info))   # envoie le retrait (TTL 0)
                log.info("Annonce retirée : %s", self.registered_name)
            except Exception as exc:
                log.warning("Retrait de l'annonce impossible : %s", exc)
        await self._close()
        if self.status == "published":
            self.status = "disabled"

    async def _close(self) -> None:
        if self._aiozc is not None:
            try:
                await self._aiozc.async_close()
            finally:
                self._aiozc = None
