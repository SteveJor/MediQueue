"""Configuration par variables d'environnement (préfixe MEDIQUEUE_) ou fichier .env."""
from __future__ import annotations

import re
from pathlib import Path
from typing import Literal

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

# Valeurs d'exemple qu'on refuse en fonctionnement réel : un secret recopié depuis .env.example est un secret public.
_PLACEHOLDER_SECRETS = {"", "change-me", "changeme", "secret", "CHANGE_ME_GENERATE_A_REAL_SECRET"}
_SERVICE_TYPE_RE = re.compile(r"^_[a-z0-9-]{1,15}\._(tcp|udp)\.local\.$")


class ConfigurationError(RuntimeError):
    """Configuration invalide : le serveur refuse de démarrer (message lisible)."""


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="MEDIQUEUE_", env_file=".env", env_file_encoding="utf-8", extra="ignore"
    )

    env: Literal["dev", "prod"] = "dev"
    database_url: str = "sqlite:///./data/mediqueue.db"

    # --- Authentification des consultants ---
    jwt_secret: str = ""
    jwt_ttl_seconds: int = Field(28800, ge=300, le=7 * 24 * 3600)   # 8 h par défaut (= expires_in du contrat)
    login_max_failures: int = Field(5, ge=1)
    login_lockout_seconds: int = Field(60, ge=1)
    ticket_rate_per_minute: int = Field(20, ge=1)                   # création de tickets, par adresse cliente

    # --- Réseau et TLS ---
    bind_host: str = "0.0.0.0"          # adresse d'ÉCOUTE (toutes les cartes) ≠ adresses ANNONCÉES (voir mdns_*)
    port: int = Field(8443, ge=0, le=65535)   # 0 = port libre choisi par le système (le port réel est annoncé)
    tls_cert_file: Path = Path("certs/server.crt")
    tls_key_file: Path = Path("certs/server.key")
    allow_insecure_http: bool = False   # DÉVELOPPEMENT UNIQUEMENT : les applications refusent le HTTP en clair

    # --- Documentation interactive (/docs) : activée par défaut seulement en dev ---
    enable_docs: bool | None = None

    # --- Découverte mDNS / DNS-SD ---
    mdns_enabled: bool = True
    mdns_service_type: str = "_mediqueue._tcp.local."
    mdns_instance_name: str = ""        # vide = « MediQueue <hôte> »
    mdns_hostname: str = ""             # vide = « <hôte>.local. »
    mdns_addresses: str = ""            # IPv4 à annoncer, séparées par des virgules ; vide = détection automatique
    mdns_exclude_adapters: str = (
        "docker,veth,br-,virbr,vbox,virtualbox,vmware,vmnet,vethernet,wsl,tailscale,zerotier,loopback,tun,tap,utun"
    )
    center_hint: str = ""               # code du centre publié dans le TXT (public) pour aider le choix côté client

    @property
    def docs_enabled(self) -> bool:
        return self.env == "dev" if self.enable_docs is None else self.enable_docs

    @property
    def tls_enabled(self) -> bool:
        return not self.allow_insecure_http

    def announce_addresses_override(self) -> list[str]:
        return [a.strip() for a in self.mdns_addresses.split(",") if a.strip()]

    def excluded_adapter_patterns(self) -> list[str]:
        return [p.strip().lower() for p in self.mdns_exclude_adapters.split(",") if p.strip()]


def validate_for_serving(settings: Settings) -> None:
    """Vérifie tout ce qui doit être juste AVANT d'ouvrir le moindre port. Lève ConfigurationError."""
    if settings.jwt_secret in _PLACEHOLDER_SECRETS or len(settings.jwt_secret) < 32:
        raise ConfigurationError(
            "MEDIQUEUE_JWT_SECRET absent, trop court (< 32 caractères) ou resté à la valeur d'exemple. "
            "Générez-en un : python -m app.cli gen-secret"
        )
    if not _SERVICE_TYPE_RE.match(settings.mdns_service_type):
        raise ConfigurationError(
            f"MEDIQUEUE_MDNS_SERVICE_TYPE invalide : {settings.mdns_service_type!r} (attendu : _nom._tcp.local.)"
        )
    if settings.tls_enabled:
        for label, path in (("certificat", settings.tls_cert_file), ("clé privée", settings.tls_key_file)):
            if not Path(path).is_file():
                raise ConfigurationError(
                    f"Fichier {label} TLS introuvable : {path}. Générez-les : python -m app.cli gen-cert"
                )
    elif settings.env == "prod":
        raise ConfigurationError("MEDIQUEUE_ALLOW_INSECURE_HTTP=true est interdit avec MEDIQUEUE_ENV=prod.")
