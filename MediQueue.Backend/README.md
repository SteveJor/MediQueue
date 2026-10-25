# MediQueue.Backend

API FastAPI de MediQueue : tickets, files d'attente, authentification des consultants. Elle **annonce automatiquement sa présence sur le réseau local** (mDNS / DNS-SD) et ne parle qu'en **HTTPS**, avec une identité cryptographique que les deux applications vérifient avant d'envoyer la moindre donnée.

> Règle métier centrale : la file n'avance que par l'appel **confirmé** d'un consultant (`POST /api/v1/consultant/queue/call-next`). Aucun minuteur, aucune tâche planifiée ne touche aux tickets.

Documents liés : [`../docs/RESEAU-DECOUVERTE-TLS.md`](../docs/RESEAU-DECOUVERTE-TLS.md) (découverte, certificats, sécurité, diagramme de séquence) · [`../docs/BACKEND-AUDIT-ET-CONCEPTION.md`](../docs/BACKEND-AUDIT-ET-CONCEPTION.md) (audit, choix, rapport) · [`../docs/CONTRAT-API.md`](../docs/CONTRAT-API.md).

## 1. Versions exactes testées

Python **3.12.3** · FastAPI 0.158.2 (Starlette 1.9.0) · Uvicorn 0.54.0 · Pydantic 2.14.1 + pydantic-settings 2.16.1 · SQLAlchemy **2**.1.6 · Alembic 1.20.0 · python-zeroconf 0.151.5 · PyJWT 2.15.1 · argon2-cffi 25.1.0 · cryptography 50.0.2 · qrcode 8.2. Tests : pytest 9.1.1, httpx2 2.16.0. Tout est épinglé dans `requirements.txt` / `requirements-dev.txt` (installation vérifiée dans un environnement vierge). Seul **Linux** a été testé pour l'hébergement ; Windows et macOS : voir § 8.

## 2. Installation

```bash
cd MediQueue.Backend
python -m venv .venv
source .venv/bin/activate            # Windows PowerShell : .venv\Scripts\Activate.ps1
pip install -r requirements.txt      # pour lancer les tests : requirements-dev.txt
```

## 3. Premier démarrage, pas à pas

```bash
cp .env.example .env                        # Windows : copy .env.example .env
python -m app.cli gen-secret                # copiez la valeur dans MEDIQUEUE_JWT_SECRET (dans .env)
python -m app.cli gen-cert                  # certificat TLS + clé ; AFFICHE LE PIN du serveur
python -m app.cli init-db                   # crée la base SQLite par les migrations Alembic
python -m app.cli seed-demo                 # centre SM-001, service, QR de démo, consultant MG-01 : mot de passe ALÉATOIRE affiché une fois
python -m app                               # démarre en HTTPS et annonce le service
```

Au démarrage vous devez lire : `Serveur à l'écoute sur 0.0.0.0:8443 (HTTPS)` puis `Service annoncé : MediQueue <hôte>._mediqueue._tcp.local., port 8443, adresses …`. Le service n'est annoncé **qu'après** avoir vérifié que le port répond réellement en TLS. Arrêt : `Ctrl+C` (l'annonce est retirée avant la fermeture du port).

Le serveur refuse de démarrer, avec un message explicite, si : le secret JWT est absent/court/laissé à l'exemple, le certificat ou la clé TLS manquent, la base n'est pas initialisée, ou si le HTTP en clair est demandé en mode `prod`.

## 4. Configuration (variables `MEDIQUEUE_*`, voir `.env.example`)

| Variable | Défaut | Rôle |
|---|---|---|
| `ENV` | `dev` | `prod` : `/docs` désactivé, HTTP en clair interdit |
| `DATABASE_URL` | `sqlite:///./data/mediqueue.db` | Base de données |
| `JWT_SECRET` | *(obligatoire)* | Signature des jetons, 32 caractères minimum |
| `JWT_TTL_SECONDS` | 28800 | Durée de vie d'un jeton (8 h) |
| `BIND_HOST` / `PORT` | `0.0.0.0` / 8443 | Écoute (toutes les cartes). `PORT=0` : port libre, le port réel est annoncé |
| `TLS_CERT_FILE` / `TLS_KEY_FILE` | `certs/server.crt/.key` | Certificat et clé du serveur |
| `ALLOW_INSECURE_HTTP` | `false` | **Développement/tests seulement.** Les applications refusent le HTTP |
| `MDNS_ENABLED` | `true` | Publication DNS-SD |
| `MDNS_SERVICE_TYPE` | `_mediqueue._tcp.local.` | Type de service annoncé |
| `MDNS_ADDRESSES` | *(auto)* | IPv4 à annoncer, séparées par des virgules |
| `MDNS_EXCLUDE_ADAPTERS` | docker, vbox, vmware, wsl, tailscale… | Cartes virtuelles jamais annoncées |
| `CENTER_HINT` | *(vide)* | Code du centre publié (public) pour aider le choix si plusieurs serveurs |
| `LOGIN_MAX_FAILURES` / `LOGIN_LOCKOUT_SECONDS` | 5 / 60 | Verrouillage temporaire après échecs |
| `TICKET_RATE_PER_MINUTE` | 20 | Création de tickets, par adresse cliente |

## 5. Donner la confiance aux applications (une fois par serveur)

Le pin est l'empreinte de la **clé publique** du serveur (`gen-cert` l'affiche ; `python -m app.cli pin` le réaffiche).

- **Patient** : imprimez dans le centre le QR du service, qui contient le pin. `python -m app.cli qr --token SM-MG-DEMO01 --out qr-medecine.png`
- **Consultant** : générez le fichier de confiance et placez-le dans le projet MAUI avant de compiler l'application :
  `python -m app.cli export-trust --center SM-001 --out ../MediQueue.Consultant/Resources/Raw/mediqueue_trust.json`

Ces deux éléments sont **publics** (pas de secret). Ne partagez jamais `certs/server.key`. Détails, renouvellement et rotation : `docs/RESEAU-DECOUVERTE-TLS.md`.

## 6. Administration (aucune route HTTP d'administration n'existe)

| Commande | Effet |
|---|---|
| `create-center --code SM-001 --name "…"` | Crée un centre |
| `create-service --center SM-001 --name "Médecine générale"` | Crée un service |
| `create-qr --center SM-001 --service "…" [--token …]` | Crée le jeton d'un QR (aléatoire si omis) |
| `create-consultant --center SM-001 --code MG-01 --service "…"` | Crée un consultant ; mot de passe saisi (masqué), 10 caractères minimum |
| `set-password` / `deactivate-consultant` / `activate-consultant` | Gestion des comptes ; la désactivation est **immédiate** (jetons déjà émis refusés) |
| `gen-cert [--reuse-key] [--days N] [--host …] [--ip …]` | Certificat TLS ; `--reuse-key` renouvelle sans changer le pin |
| `qr`, `export-trust`, `pin` | QR du centre, fichier de confiance Consultant, empreinte |
| `show-discovery` | Cartes réseau et adresses qui seraient annoncées |
| `seed-demo`, `init-db`, `gen-secret` | Démonstration, migrations, secret |

Il n'y a **aucun mot de passe universel** : `seed-demo` tire un mot de passe aléatoire (affiché une seule fois). Le `demo1234` de la simulation des applications n'existe pas ici.

## 7. Réseau, pare-feu et découverte

Le serveur écoute sur toutes les cartes, mais n'**annonce** que les IPv4 des cartes réseau réelles (ni loopback, ni link-local, ni Docker/VirtualBox/WSL/VPN). Vérifiez avec `python -m app.cli show-discovery`.

Ports à autoriser en entrée (réseau **privé** uniquement) : **TCP 8443** (API) et **UDP 5353** (mDNS).

```powershell
# Windows (PowerShell administrateur)
New-NetFirewallRule -DisplayName "MediQueue API"  -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow -Profile Private
New-NetFirewallRule -DisplayName "MediQueue mDNS" -Direction Inbound -Protocol UDP -LocalPort 5353 -Action Allow -Profile Private
```
```bash
# Linux (ufw)
sudo ufw allow 8443/tcp && sudo ufw allow 5353/udp
```

La découverte **ne fonctionne pas sur tous les Wi-Fi** : isolation des clients (« AP isolation »), filtrage du multicast ou VLAN séparés la bloquent. Dans ce cas les applications affichent « Serveur MediQueue introuvable » ; il n'y a volontairement **pas** de repli sur une adresse IP codée en dur. Solution : un réseau (ou un point d'accès dédié) qui laisse passer le multicast entre appareils.

## 8. Hébergement : différences entre systèmes (seul Linux testé)

- **Linux** : testé. Avahi, s'il tourne, coexiste avec python-zeroconf.
- **Windows** : non testé ici. Même commandes ; ouvrir les ports ci-dessus ; vérifier que le Wi-Fi est classé « réseau privé » ; `python-zeroconf` est supporté sous Windows.
- **macOS** : non testé ici. Accepter l'invite « autoriser les connexions entrantes » pour Python ; Bonjour coexiste.

## 9. Tests

```bash
pip install -r requirements-dev.txt
python -m pytest                       # 69 tests : API, sécurité, concurrence, migrations, TLS réel, cycle de vie, mDNS réel
python -m pytest -m "not network"      # sans le test mDNS réel
```

Le test `test_mdns_real.py` publie réellement le service sur une interface et le redécouvre avec un second client ; il est **ignoré avec explication** si le multicast n'est pas disponible. Il tourne sur **un seul ordinateur** : il ne remplace pas l'essai avec un vrai téléphone (procédure : `docs/RESEAU-DECOUVERTE-TLS.md` § 9). Test de bout en bout avec les vrais clients C# : `tools/IntegrationCheck/run_e2e.sh`.

## 10. Évolutions prévues

- **PostgreSQL** si plusieurs serveurs ou accès concurrents lourds : le code est portable (SQLAlchemy 2, verrou `FOR UPDATE` déjà prévu dans `app/db/session.py`) ; il faudra adapter `DATABASE_URL`, `requirements.txt` et rejouer les migrations.
- Les limiteurs de débit sont **en mémoire** (un seul processus) ; avec plusieurs processus, passer à un stockage partagé.
- Pas de temps réel poussé : les applications relisent l'état toutes les 5 à 8 secondes (suffisant pour une file d'attente, bien plus simple que SSE ou WebSocket).

## 11. Dépannage

| Symptôme | Piste |
|---|---|
| `MEDIQUEUE_JWT_SECRET absent…` | `python -m app.cli gen-secret`, puis le coller dans `.env` |
| `Fichier certificat TLS introuvable` | `python -m app.cli gen-cert` |
| `Base de données absente ou non initialisée` | `python -m app.cli init-db` |
| `Aucune adresse IPv4 de réseau local détectée` | Connectez-vous au Wi-Fi/Ethernet, ou renseignez `MEDIQUEUE_MDNS_ADDRESSES` |
| Le téléphone ne trouve pas le serveur | Même réseau ? Pare-feu (TCP 8443 + UDP 5353) ? Isolation des clients du Wi-Fi ? Ouvrez `https://<IP>:8443/api/v1/health` (le navigateur avertira du certificat auto-signé : normal) |
| Les applications disent « identité non vérifiée » | Le pin du QR / du fichier de confiance ne correspond pas à la clé du serveur (certificat régénéré avec une nouvelle clé ?) → réimprimer le QR / réexporter la confiance |
| Certificat expiré | `python -m app.cli gen-cert --reuse-key` (même pin), puis redémarrer |
