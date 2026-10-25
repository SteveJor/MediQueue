"""Moteur SQLAlchemy 2 + transactions.

SQLite : chaque transaction d'ÉCRITURE démarre par BEGIN IMMEDIATE (verrou d'écriture pris tout de suite),
ce qui rend « lire le prochain ticket puis le modifier » atomique entre requêtes concurrentes.
PostgreSQL (évolution future) : même code, le verrou est pris par un SELECT ... FOR UPDATE sur le service.
"""
from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager

from sqlalchemy import Engine, create_engine, event, select
from sqlalchemy.orm import Session, sessionmaker


def make_engine(database_url: str) -> Engine:
    is_sqlite = database_url.startswith("sqlite")
    engine = create_engine(
        database_url,
        connect_args={"check_same_thread": False, "timeout": 15} if is_sqlite else {},
        future=True,
    )
    if is_sqlite:

        @event.listens_for(engine, "connect")
        def _on_connect(dbapi_connection, _record):
            dbapi_connection.isolation_level = None      # c'est nous qui émettons BEGIN (voir ci-dessous)
            cursor = dbapi_connection.cursor()
            cursor.execute("PRAGMA foreign_keys=ON")     # contraintes d'intégrité réellement appliquées
            cursor.execute("PRAGMA journal_mode=WAL")    # les lectures ne bloquent pas l'écriture
            cursor.execute("PRAGMA busy_timeout=15000")
            cursor.close()

        @event.listens_for(engine, "begin")
        def _on_begin(conn):
            immediate = conn.get_execution_options().get("immediate", False)
            conn.exec_driver_sql("BEGIN IMMEDIATE" if immediate else "BEGIN")

    return engine


def make_session_factory(engine: Engine) -> sessionmaker[Session]:
    return sessionmaker(bind=engine, expire_on_commit=False, autoflush=False)


def begin_write(db: Session) -> None:
    """À appeler AVANT toute lecture d'une opération qui modifie la file : ouvre une transaction BEGIN IMMEDIATE.

    Si une transaction de lecture est déjà ouverte, elle est terminée d'abord (sinon le verrou serait pris trop tard).
    """
    if db.in_transaction():
        db.rollback()
    db.connection(execution_options={"immediate": True})


def lock_service(db: Session, service_id: int) -> None:
    """PostgreSQL : verrouille la ligne du service pour sérialiser les écritures sur sa file. SQLite : inutile
    (le verrou d'écriture du fichier est déjà pris par BEGIN IMMEDIATE)."""
    if db.bind is not None and db.bind.dialect.name != "sqlite":
        from app.models import Service
        db.execute(select(Service.id).where(Service.id == service_id).with_for_update())


@contextmanager
def session_scope(factory: sessionmaker[Session]) -> Iterator[Session]:
    session = factory()
    try:
        yield session
        session.commit()
    except Exception:
        session.rollback()
        raise
    finally:
        session.close()
