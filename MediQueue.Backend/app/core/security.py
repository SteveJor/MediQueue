"""Hachage des mots de passe (argon2id) et jetons JWT d'accès des consultants."""
from __future__ import annotations

import secrets
import time
import uuid
from dataclasses import dataclass

import jwt
from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError, VerifyMismatchError

from app.core.config import Settings

JWT_ALGORITHM = "HS256"      # liste blanche explicite à la vérification (pas de confusion d'algorithme, pas de « none »)
JWT_ISSUER = "mediqueue"
JWT_AUDIENCE = "mediqueue-consultant"

_hasher = PasswordHasher()
# Hachage factice : vérifié quand l'identifiant n'existe pas, pour que le temps de réponse ne révèle pas s'il existe.
_DUMMY_HASH = _hasher.hash("mot-de-passe-factice-pour-lisser-le-temps-de-reponse")


def hash_password(password: str) -> str:
    return _hasher.hash(password)


def verify_password(password_hash: str | None, password: str) -> bool:
    try:
        return _hasher.verify(password_hash or _DUMMY_HASH, password) and password_hash is not None
    except (VerifyMismatchError, VerificationError, InvalidHashError):
        return False


def generate_password(length: int = 16) -> str:
    alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789"   # sans caractères ambigus (0/O, 1/l/I)
    return "".join(secrets.choice(alphabet) for _ in range(length))


class TokenError(Exception):
    """Jeton absent, mal formé, falsifié ou expiré."""


@dataclass(frozen=True)
class TokenClaims:
    consultant_id: int
    center_id: int
    service_id: int


def create_access_token(settings: Settings, *, consultant_id: int, center_id: int, service_id: int) -> tuple[str, int]:
    now = int(time.time())
    claims = {
        "iss": JWT_ISSUER, "aud": JWT_AUDIENCE, "sub": str(consultant_id), "role": "consultant",
        "cid": center_id, "sid": service_id, "iat": now, "exp": now + settings.jwt_ttl_seconds,
        "jti": uuid.uuid4().hex,
    }
    return jwt.encode(claims, settings.jwt_secret, algorithm=JWT_ALGORITHM), settings.jwt_ttl_seconds


def decode_access_token(settings: Settings, token: str) -> TokenClaims:
    try:
        data = jwt.decode(
            token, settings.jwt_secret, algorithms=[JWT_ALGORITHM], audience=JWT_AUDIENCE, issuer=JWT_ISSUER,
            options={"require": ["exp", "iat", "sub", "aud", "iss"]},
        )
        if data.get("role") != "consultant":
            raise TokenError("rôle inattendu")
        return TokenClaims(int(data["sub"]), int(data["cid"]), int(data["sid"]))
    except (jwt.PyJWTError, KeyError, ValueError, TypeError) as exc:
        raise TokenError(type(exc).__name__) from exc
