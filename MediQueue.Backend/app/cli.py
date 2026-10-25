"""Administration : `python -m app.cli <commande>`. Aucune route HTTP d'administration n'existe."""
from __future__ import annotations

import argparse
import getpass
import json
import secrets
import sys
from pathlib import Path

from app.core.config import Settings
from app.core.security import generate_password
from app.db.session import make_engine, make_session_factory, session_scope
from app.services import admin_service as admin

ROOT = Path(__file__).resolve().parent.parent
DEMO = {"center": "SM-001", "center_name": "Centre Saint-Martin", "service": "Médecine générale",
        "login": "MG-01", "token": "SM-MG-DEMO01"}


def run_migrations(database_url: str, revision: str = "head") -> None:
    from alembic import command
    from alembic.config import Config

    cfg = Config(str(ROOT / "alembic.ini"))
    cfg.set_main_option("script_location", str(ROOT / "alembic"))
    cfg.attributes["database_url"] = database_url
    command.upgrade(cfg, revision)


def _session(settings: Settings):
    return session_scope(make_session_factory(make_engine(settings.database_url)))


def _ask_password() -> str:
    first = getpass.getpass("Mot de passe (10 caractères minimum) : ")
    if first != getpass.getpass("Confirmez : "):
        raise admin.AdminError("Les deux saisies diffèrent.")
    return first


# ───────────── commandes ─────────────

def cmd_gen_secret(_: argparse.Namespace, __: Settings) -> None:
    print(secrets.token_urlsafe(48))


def cmd_init_db(_: argparse.Namespace, settings: Settings) -> None:
    Path(ROOT / "data").mkdir(exist_ok=True)
    run_migrations(settings.database_url)
    print("Base initialisée (migrations appliquées).")


def cmd_seed_demo(args: argparse.Namespace, settings: Settings) -> None:
    """Données de démonstration. Le mot de passe du consultant est ALÉATOIRE (affiché une seule fois) : pas de mot de passe universel."""
    with _session(settings) as db:
        try:
            admin.create_center(db, DEMO["center"], DEMO["center_name"])
            admin.create_service(db, DEMO["center"], DEMO["service"])
            admin.create_qr(db, DEMO["center"], DEMO["service"], token=DEMO["token"])
            password = args.password or generate_password()
            admin.create_consultant(db, DEMO["center"], DEMO["login"], DEMO["service"], password)
        except admin.AdminError as exc:
            print(f"Données de démonstration déjà présentes ou incohérentes : {exc}")
            return
    print(f"Centre {DEMO['center']} · service {DEMO['service']} · jeton de QR {DEMO['token']}")
    print(f"Consultant {DEMO['login']} — mot de passe (affiché une seule fois) : {password}")


def cmd_create_center(args, settings):
    with _session(settings) as db:
        admin.create_center(db, args.code, args.name, args.info)
    print("Centre créé.")


def cmd_create_service(args, settings):
    with _session(settings) as db:
        admin.create_service(db, args.center, args.name)
    print("Service créé.")


def cmd_create_qr(args, settings):
    with _session(settings) as db:
        qr = admin.create_qr(db, args.center, args.service, args.token)
        token = qr.token
    print(f"Jeton de QR : {token}")
    print("Pour imprimer le QR : python -m app.cli qr --token", token)


def cmd_create_consultant(args, settings):
    password = args.password or _ask_password()
    with _session(settings) as db:
        admin.create_consultant(db, args.center, args.code, args.service, password)
    print("Consultant créé.")


def cmd_set_password(args, settings):
    password = _ask_password()
    with _session(settings) as db:
        admin.set_password(db, args.center, args.code, password)
    print("Mot de passe modifié.")


def cmd_deactivate(args, settings):
    with _session(settings) as db:
        admin.set_consultant_active(db, args.center, args.code, False)
    print("Consultant désactivé (effet immédiat : ses jetons existants sont refusés).")


def cmd_activate(args, settings):
    with _session(settings) as db:
        admin.set_consultant_active(db, args.center, args.code, True)
    print("Consultant réactivé.")


def cmd_gen_cert(args, settings):
    from app.discovery.network import list_adapters, select_announce_addresses
    from app.tls.certs import default_hostnames, generate_self_signed
    from app.tls.pin import certificate_pin

    ips = args.ip or select_announce_addresses(list_adapters(), settings.excluded_adapter_patterns(),
                                               settings.announce_addresses_override())
    hostnames = default_hostnames() + (args.host or [])
    generate_self_signed(settings.tls_cert_file, settings.tls_key_file, hostnames=hostnames, ips=ips,
                         days=args.days, reuse_key=args.reuse_key)
    print(f"Certificat : {settings.tls_cert_file}\nClé privée : {settings.tls_key_file} (à ne JAMAIS partager)")
    print(f"Noms : {', '.join(hostnames)} · adresses : {', '.join(ips) or '(aucune détectée)'} + 127.0.0.1")
    print(f"PIN du serveur (à placer dans le QR et dans la confiance du Consultant) : {certificate_pin(settings.tls_cert_file)}")
    if args.reuse_key:
        print("Clé conservée : le pin n'a pas changé, les QR déjà imprimés restent valables.")


def cmd_pin(args, settings):
    from app.tls.pin import certificate_pin, key_pin

    print("pin du certificat actuel :", certificate_pin(settings.tls_cert_file))
    for backup in args.backup_key or []:
        print(f"pin de la clé de secours {backup} :", key_pin(backup))


def _pins_or_default(args, settings) -> list[str]:
    from app.tls.pin import certificate_pin

    return args.pin or [certificate_pin(settings.tls_cert_file)]


def cmd_qr(args, settings):
    import qrcode

    pins = _pins_or_default(args, settings)
    payload = f"mediqueue://queue/{args.token}" + "".join(f"{'?' if i == 0 else '&'}pin={p}" for i, p in enumerate(pins))
    img = qrcode.make(payload, error_correction=qrcode.constants.ERROR_CORRECT_M, box_size=10, border=4)
    out = Path(args.out)
    img.save(out)
    print(f"QR écrit : {out}\nContenu : {payload}")


def cmd_export_trust(args, settings):
    """Fichier de confiance (public, non secret) à embarquer dans l'app Consultant : Resources/Raw/mediqueue_trust.json."""
    data = {"version": 1, "pins": _pins_or_default(args, settings), "center_code": args.center or ""}
    Path(args.out).write_text(json.dumps(data, indent=2), encoding="utf-8")
    print(f"Fichier de confiance écrit : {args.out}")


def cmd_show_discovery(_, settings):
    from app.discovery.network import list_adapters, select_announce_addresses

    adapters = list_adapters()
    for a in adapters:
        print(f"  carte : {a.name} → {', '.join(a.ipv4) or '(pas d’IPv4)'}")
    chosen = select_announce_addresses(adapters, settings.excluded_adapter_patterns(), settings.announce_addresses_override())
    print("Adresses qui seraient ANNONCÉES :", ", ".join(chosen) or "(aucune)")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m app.cli", description="Administration de MediQueue")
    sub = p.add_subparsers(dest="command", required=True)

    sub.add_parser("gen-secret", help="génère un secret JWT").set_defaults(fn=cmd_gen_secret)
    sub.add_parser("init-db", help="applique les migrations").set_defaults(fn=cmd_init_db)
    s = sub.add_parser("seed-demo", help="données de démonstration (mot de passe aléatoire)")
    s.add_argument("--password", help="mot de passe du consultant de démo (sinon aléatoire)")
    s.set_defaults(fn=cmd_seed_demo)

    s = sub.add_parser("create-center"); s.add_argument("--code", required=True); s.add_argument("--name", required=True)
    s.add_argument("--info"); s.set_defaults(fn=cmd_create_center)
    s = sub.add_parser("create-service"); s.add_argument("--center", required=True); s.add_argument("--name", required=True)
    s.set_defaults(fn=cmd_create_service)
    s = sub.add_parser("create-qr"); s.add_argument("--center", required=True); s.add_argument("--service", required=True)
    s.add_argument("--token"); s.set_defaults(fn=cmd_create_qr)
    s = sub.add_parser("create-consultant"); s.add_argument("--center", required=True); s.add_argument("--code", required=True)
    s.add_argument("--service", required=True); s.add_argument("--password", help="évitez : sinon saisie masquée")
    s.set_defaults(fn=cmd_create_consultant)
    for name, fn in (("set-password", cmd_set_password), ("deactivate-consultant", cmd_deactivate),
                     ("activate-consultant", cmd_activate)):
        s = sub.add_parser(name); s.add_argument("--center", required=True); s.add_argument("--code", required=True)
        s.set_defaults(fn=fn)

    s = sub.add_parser("gen-cert", help="certificat TLS auto-signé + affichage du pin")
    s.add_argument("--host", action="append", help="nom DNS supplémentaire (répétable)")
    s.add_argument("--ip", action="append", help="adresse IP supplémentaire (répétable) ; sinon détection")
    s.add_argument("--days", type=int, default=730)
    s.add_argument("--reuse-key", action="store_true", help="renouvelle le certificat en gardant la clé (pin inchangé)")
    s.set_defaults(fn=cmd_gen_cert)
    s = sub.add_parser("pin", help="affiche le pin du certificat (et des clés de secours)")
    s.add_argument("--backup-key", action="append"); s.set_defaults(fn=cmd_pin)
    s = sub.add_parser("qr", help="image PNG du QR d'un service, avec le(s) pin(s) du serveur")
    s.add_argument("--token", required=True); s.add_argument("--pin", action="append")
    s.add_argument("--out", default="qr.png"); s.set_defaults(fn=cmd_qr)
    s = sub.add_parser("export-trust", help="fichier de confiance pour l'app Consultant")
    s.add_argument("--pin", action="append"); s.add_argument("--center"); s.add_argument("--out", required=True)
    s.set_defaults(fn=cmd_export_trust)
    sub.add_parser("show-discovery", help="cartes réseau et adresses qui seraient annoncées").set_defaults(fn=cmd_show_discovery)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        args.fn(args, Settings())
    except admin.AdminError as exc:
        print(f"Erreur : {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
