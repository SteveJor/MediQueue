"""Limitation de débit en mémoire (un seul processus). Limite connue : remise à zéro au redémarrage."""
from __future__ import annotations

import threading
import time
from collections import defaultdict, deque


class SlidingWindowLimiter:
    """Au plus `limit` événements par fenêtre glissante de `window` secondes et par clé."""

    def __init__(self, limit: int, window: float = 60.0, clock=time.monotonic):
        self.limit, self.window, self._clock = limit, window, clock
        self._events: dict[str, deque[float]] = defaultdict(deque)
        self._lock = threading.Lock()

    def hit(self, key: str) -> int:
        """Enregistre un événement. Renvoie 0 si autorisé, sinon le nombre de secondes à attendre."""
        now = self._clock()
        with self._lock:
            q = self._events[key]
            while q and now - q[0] >= self.window:
                q.popleft()
            if len(q) >= self.limit:
                return int(self.window - (now - q[0])) + 1
            q.append(now)
            return 0


class LoginThrottle:
    """Verrouille temporairement une clé après `max_failures` échecs ; un succès remet le compteur à zéro."""

    def __init__(self, max_failures: int, lockout_seconds: int, clock=time.monotonic):
        self.max_failures, self.lockout, self._clock = max_failures, lockout_seconds, clock
        self._failures: dict[str, list[float]] = defaultdict(list)
        self._lock = threading.Lock()

    def retry_after(self, key: str) -> int:
        now = self._clock()
        with self._lock:
            recent = [t for t in self._failures[key] if now - t < self.lockout]
            self._failures[key] = recent
            if len(recent) >= self.max_failures:
                return int(self.lockout - (now - recent[0])) + 1
            return 0

    def record_failure(self, key: str) -> None:
        with self._lock:
            self._failures[key].append(self._clock())

    def reset(self, key: str) -> None:
        with self._lock:
            self._failures.pop(key, None)
