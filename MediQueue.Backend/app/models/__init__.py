"""Modèle de données : Centre, Service, Consultant, QR Code, Ticket."""
from __future__ import annotations

import enum
from datetime import datetime, timezone

from sqlalchemy import (
    Boolean, CheckConstraint, DateTime, ForeignKey, ForeignKeyConstraint, Index, Integer, String, Text,
    UniqueConstraint, text,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db.base import Base


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class TicketStatus(str, enum.Enum):
    WAITING = "WAITING"
    IN_CONSULTATION = "IN_CONSULTATION"
    COMPLETED = "COMPLETED"
    CANCELLED = "CANCELLED"


class Center(Base):
    __tablename__ = "centers"

    id: Mapped[int] = mapped_column(primary_key=True)
    code: Mapped[str] = mapped_column(String(32), unique=True)       # ex. SM-001 (saisi par le consultant)
    name: Mapped[str] = mapped_column(String(120))
    info: Mapped[str | None] = mapped_column(Text, default=None)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)

    services: Mapped[list["Service"]] = relationship(back_populates="center")


class Service(Base):
    __tablename__ = "services"
    __table_args__ = (
        UniqueConstraint("center_id", "name", name="uq_service_center_name"),
        UniqueConstraint("center_id", "id", name="uq_service_center_id"),   # cible des clés étrangères composites
    )

    id: Mapped[int] = mapped_column(primary_key=True)
    center_id: Mapped[int] = mapped_column(ForeignKey("centers.id"))
    name: Mapped[str] = mapped_column(String(120))
    is_active: Mapped[bool] = mapped_column(Boolean, default=True)

    center: Mapped[Center] = relationship(back_populates="services")


class Consultant(Base):
    __tablename__ = "consultants"
    __table_args__ = (
        UniqueConstraint("center_id", "login_code", name="uq_consultant_center_login"),
        # Le service doit appartenir au centre du consultant (garanti par la base, pas seulement par le code).
        ForeignKeyConstraint(["center_id", "service_id"], ["services.center_id", "services.id"],
                             name="fk_consultant_service_in_center"),
    )

    id: Mapped[int] = mapped_column(primary_key=True)
    center_id: Mapped[int] = mapped_column(Integer)
    service_id: Mapped[int] = mapped_column(Integer)
    login_code: Mapped[str] = mapped_column(String(32))               # ex. MG-01
    password_hash: Mapped[str] = mapped_column(String(255))           # argon2id, jamais le mot de passe
    is_active: Mapped[bool] = mapped_column(Boolean, default=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)

    center: Mapped[Center] = relationship(foreign_keys=[center_id], primaryjoin="Consultant.center_id == Center.id",
                                          viewonly=True)
    service: Mapped[Service] = relationship(foreign_keys=[service_id], primaryjoin="Consultant.service_id == Service.id",
                                            viewonly=True)


class QRCode(Base):
    __tablename__ = "qr_codes"
    __table_args__ = (
        ForeignKeyConstraint(["center_id", "service_id"], ["services.center_id", "services.id"],
                             name="fk_qr_service_in_center"),
    )

    id: Mapped[int] = mapped_column(primary_key=True)
    token: Mapped[str] = mapped_column(String(64), unique=True)
    center_id: Mapped[int] = mapped_column(Integer)
    service_id: Mapped[int] = mapped_column(Integer)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)

    center: Mapped[Center] = relationship(foreign_keys=[center_id], primaryjoin="QRCode.center_id == Center.id",
                                          viewonly=True)
    service: Mapped[Service] = relationship(foreign_keys=[service_id], primaryjoin="QRCode.service_id == Service.id",
                                            viewonly=True)


class Ticket(Base):
    __tablename__ = "tickets"
    __table_args__ = (
        UniqueConstraint("service_id", "number", name="uq_ticket_service_number"),
        CheckConstraint("status IN ('WAITING','IN_CONSULTATION','COMPLETED','CANCELLED')", name="ck_ticket_status"),
        ForeignKeyConstraint(["center_id", "service_id"], ["services.center_id", "services.id"],
                             name="fk_ticket_service_in_center"),
        Index("ix_ticket_queue", "service_id", "status", "number"),
        # Au plus UN patient en consultation par service, garanti par la base.
        Index("uq_ticket_one_in_consultation", "service_id", unique=True,
              sqlite_where=text("status = 'IN_CONSULTATION'"), postgresql_where=text("status = 'IN_CONSULTATION'")),
    )

    id: Mapped[str] = mapped_column(String(32), primary_key=True)     # 128 bits aléatoires, hexadécimal : non devinable
    center_id: Mapped[int] = mapped_column(Integer)
    service_id: Mapped[int] = mapped_column(Integer)
    number: Mapped[int] = mapped_column(Integer)
    patient_name: Mapped[str | None] = mapped_column(String(40), default=None)
    status: Mapped[str] = mapped_column(String(20), default=TicketStatus.WAITING.value)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
    called_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), default=None)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), default=None)
    cancelled_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), default=None)
