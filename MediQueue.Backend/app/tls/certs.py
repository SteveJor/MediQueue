"""Génération du certificat TLS du serveur (auto-signé, clé ECDSA P-256) et de ses SAN."""
from __future__ import annotations

import datetime as dt
import ipaddress
import os
import socket
from pathlib import Path

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import ExtendedKeyUsageOID, NameOID

from app.tls.pin import load_private_key


def generate_self_signed(
    cert_path: Path, key_path: Path, *, hostnames: list[str], ips: list[str], days: int = 730, reuse_key: bool = False
) -> None:
    """Écrit certificat + clé. Avec reuse_key, la clé existante est conservée : le pin ne change pas au renouvellement."""
    if reuse_key and Path(key_path).is_file():
        key = load_private_key(key_path)
    else:
        key = ec.generate_private_key(ec.SECP256R1())

    san: list[x509.GeneralName] = []
    for name in dict.fromkeys(h for h in hostnames if h):
        san.append(x509.DNSName(name))
    for ip in dict.fromkeys(ips + ["127.0.0.1"]):
        san.append(x509.IPAddress(ipaddress.ip_address(ip)))

    now = dt.datetime.now(dt.timezone.utc)
    subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "MediQueue Server")])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject).issuer_name(subject)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - dt.timedelta(minutes=5))
        .not_valid_after(now + dt.timedelta(days=days))
        .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
        .add_extension(x509.KeyUsage(digital_signature=True, content_commitment=False, key_encipherment=False,
                                     data_encipherment=False, key_agreement=False, key_cert_sign=False,
                                     crl_sign=False, encipher_only=False, decipher_only=False), critical=True)
        .add_extension(x509.ExtendedKeyUsage([ExtendedKeyUsageOID.SERVER_AUTH]), critical=False)
        .add_extension(x509.SubjectAlternativeName(san), critical=False)
        .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False)
        .sign(key, hashes.SHA256())
    )

    Path(cert_path).parent.mkdir(parents=True, exist_ok=True)
    Path(key_path).parent.mkdir(parents=True, exist_ok=True)
    Path(cert_path).write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    key_bytes = key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
                                  serialization.NoEncryption())
    fd = os.open(key_path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)   # clé privée lisible par le propriétaire seulement
    with os.fdopen(fd, "wb") as handle:
        handle.write(key_bytes)


def default_hostnames() -> list[str]:
    host = socket.gethostname().split(".")[0]
    return ["mediqueue.local", "localhost", host, f"{host}.local"]
