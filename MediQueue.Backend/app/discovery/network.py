"""Choix des adresses ANNONCÉES aux clients (≠ adresse d'écoute du serveur).

Le serveur écoute sur 0.0.0.0 (toutes les cartes) ; mais annoncer 127.0.0.1, une adresse link-local ou l'adresse d'une
carte virtuelle (Docker, VirtualBox, WSL, VPN) ferait découvrir aux téléphones une adresse injoignable.
On n'annonce donc que les IPv4 des cartes réseau « réelles ». Limite documentée : si plusieurs cartes réelles sont
actives (Wi-Fi + Ethernet), toutes sont annoncées et le client choisit celle de son sous-réseau.
"""
from __future__ import annotations

import ipaddress
from dataclasses import dataclass


@dataclass(frozen=True)
class AdapterInfo:
    name: str
    ipv4: tuple[str, ...]


def list_adapters() -> list[AdapterInfo]:
    import ifaddr   # dépendance de zeroconf

    result = []
    for adapter in ifaddr.get_adapters():
        ips = tuple(ip.ip for ip in adapter.ips if ip.is_IPv4 and isinstance(ip.ip, str))
        label = f"{adapter.name} {getattr(adapter, 'nice_name', '')}".strip()
        result.append(AdapterInfo(label, ips))
    return result


def select_announce_addresses(
    adapters: list[AdapterInfo], exclude_patterns: list[str], override: list[str] | None = None
) -> list[str]:
    if override:
        return list(dict.fromkeys(override))

    chosen: list[ipaddress.IPv4Address] = []
    for adapter in adapters:
        label = adapter.name.lower()
        if any(pattern in label for pattern in exclude_patterns):
            continue
        for raw in adapter.ipv4:
            ip = ipaddress.ip_address(raw)
            if ip.is_loopback or ip.is_link_local or ip.is_unspecified or ip.is_multicast:
                continue
            chosen.append(ip)
    chosen.sort(key=lambda ip: (not ip.is_private, str(ip)))     # réseaux privés d'abord
    return list(dict.fromkeys(str(ip) for ip in chosen))
