"""Empreinte de clé publique (SPKI) : l'identité cryptographique du serveur.

Pin = SHA-256 de l'encodage DER du SubjectPublicKeyInfo, en base64url sans remplissage (43 caractères).
C'est exactement ce que calculent les applications MAUI (X509Certificate2.PublicKey.ExportSubjectPublicKeyInfo).
Le pin identifie la CLÉ, pas le certificat : on peut renouveler le certificat sans changer le pin (même clé).
"""
from __future__ import annotations

import base64
import hashlib
from pathlib import Path

from cryptography import x509
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.types import PrivateKeyTypes, PublicKeyTypes


def spki_sha256(public_key: PublicKeyTypes) -> bytes:
    der = public_key.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    return hashlib.sha256(der).digest()


def to_pin(digest: bytes) -> str:
    return base64.urlsafe_b64encode(digest).rstrip(b"=").decode("ascii")


def pin_hint(digest: bytes) -> str:
    """16 premiers caractères hexadécimaux : simple INDICE public (TXT DNS-SD) pour repérer le bon serveur.
    Ce n'est ni un secret ni une preuve d'identité : seule la vérification TLS du pin complet authentifie."""
    return digest[:8].hex()


def load_certificate(path: str | Path) -> x509.Certificate:
    return x509.load_pem_x509_certificate(Path(path).read_bytes())


def load_private_key(path: str | Path) -> PrivateKeyTypes:
    return serialization.load_pem_private_key(Path(path).read_bytes(), password=None)


def certificate_pin(path: str | Path) -> str:
    return to_pin(spki_sha256(load_certificate(path).public_key()))


def certificate_hint(path: str | Path) -> str:
    return pin_hint(spki_sha256(load_certificate(path).public_key()))


def key_pin(path: str | Path) -> str:
    return to_pin(spki_sha256(load_private_key(path).public_key()))
