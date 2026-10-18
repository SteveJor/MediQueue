# MediQueue — la file d'attente numérique des centres de santé

> Atelier de développement mobile — ENSPD · Activité n°7 (examen, étape 4)
> Auteur : Steve Jordan Nongning Lele — GL4

## 1. Le projet en bref

MediQueue remplace la file d'attente physique d'un centre de santé par une **file numérique**.

- Le **patient** scanne le QR code du service, reçoit un ticket numéroté et suit sa position depuis son téléphone, **sans créer de compte**.
- Le **consultant** appelle le patient suivant depuis son propre téléphone, par une action **manuelle et confirmée**.

Règle métier centrale : **la file n'avance que par l'action confirmée du consultant, jamais avec le temps.**

## 2. Pourquoi deux applications et non une seule ?

Le projet est volontairement divisé en **deux applications distinctes**, pour un besoin de **sécurité** et de **confidentialité** :

- **Séparation stricte des rôles.** L'application Patient ne peut jamais faire avancer la file ni voir les données d'un autre patient. Seule l'application Consultant peut appeler le patient suivant.
- **Aucun écran de gestion dans l'application publique.** Si les deux interfaces étaient dans une même application, n'importe qui pourrait la télécharger et en examiner le code de gestion de la file, même protégé par un mot de passe. Ici, le code de l'application Consultant n'est jamais distribué au grand public.
- **Surface d'attaque réduite.** L'application Patient n'a ni compte ni mot de passe : elle ne conserve qu'un ticket (numéro, centre, service, nom facultatif). L'application Consultant, elle, exige une connexion et conserve sa session dans un stockage chiffré (Android Keystore).
- **Distribution contrôlée du côté Consultant.** Modèle prévu : l'application Consultant n'est remise qu'aux centres ayant **payé leur licence**. Elle est envoyée aux consultants via un **lien Play Store à accès restreint** : elle n'apparaît donc pas publiquement dans les stores. L'application Patient, elle, est publique.
- **Deux identités, deux thèmes.** Identifiants d'application distincts (`com.mediqueue.patient` et `com.mediqueue.consultant`), palette verte pour le patient et bleue pour le consultant : le personnel ne peut pas confondre les deux applications.
- **Évolution indépendante.** Chaque application peut être corrigée, mise à jour et publiée séparément.

## 3. Les deux applications

| | **MediQueue.Patient** | **MediQueue.Consultant** |
|---|---|---|
| Public | Patients (téléchargement public) | Personnel du centre (licence payante, accès restreint) |
| Rôle | Rejoindre la file, suivre sa position | Voir la file, appeler le patient suivant |
| Écrans | Accueil · Scan du QR code · Ticket prêt (nom facultatif) | Connexion · Tableau de bord |
| Composants | Bottom sheet (rejoindre une file, annuler) | Bottom sheet (confirmer l'appel), notification « Patient N°… appelé » |
| Compte | Aucun | Identifiants fournis au centre |
| Couleur | Vert (`#167B69`) | Bleu médical (`#2F6F9E`) |

## 4. Fonctionnalités

- Scan **réel** du QR code par la caméra (Google ML Kit).
- Ticket numéroté avec statut (en attente, en consultation) et position dans la file.
- Actualisation automatique en lecture seule (la file n'avance jamais seule).
- Annulation volontaire du ticket par le patient, avec confirmation.
- Connexion du consultant, session conservée de façon chiffrée.
- Appel du patient suivant avec confirmation, puis notification.
- Données conservées localement sur l'appareil (le ticket survit à la fermeture de l'application).
- **Mode simulation** : l'application fonctionne sans serveur, avec des données fictives ; un simple réglage (`AppConfig.Mode`) bascule vers l'API réelle.

## 5. Choix techniques

- **.NET 10 MAUI** (C#), cible Android.
- **MVVM écrit à la main** (`ViewModelBase`, `Command`, navigation Shell, injection de dépendances) : aucune bibliothèque MVVM.
- **Services derrière des interfaces** (`IQueueService`, `IConsultantService`) avec deux implémentations : simulation (`Mock…`) et API réelle (`Api…`).
- Scan QR : `BarcodeScanning.Native.Maui` 3.1.0.
- Backend prévu : Python FastAPI + SQLite. Le client API des deux applications est déjà écrit.

## 6. Structure du dépôt

```
MediQueue/
├── README.md                  ← ce fichier
├── MediQueue.sln              ← solution Visual Studio (les deux applications)
├── MediQueue.Patient/         ← application Patient (publique)
├── MediQueue.Consultant/      ← application Consultant (licence, accès restreint)
└── qr_demo/                   ← QR codes de démonstration pour tester le scan
```

## 7. Lancer une application (mode simulation, sans serveur)

**Prérequis** : Visual Studio avec la charge de travail « Développement .NET Multiplateforme » (.NET MAUI), le SDK .NET 10, et un émulateur Android (API 34 ou plus) ou un téléphone Android en mode développeur.

1. Ouvrir `MediQueue.sln` dans Visual Studio.
2. Choisir le projet de démarrage (`MediQueue.Patient` ou `MediQueue.Consultant`) et la cible **Android**.
3. Laisser la restauration NuGet se terminer, puis lancer ▶.
4. L'application démarre en **mode simulation** : aucun serveur n'est nécessaire. Un encadré « MODE SIMULATION » en bas de l'écran permet de jouer le rôle de l'autre application.

Les deux applications ont des identifiants différents : elles s'installent côte à côte sur le même appareil.

### Données de démonstration

- **Consultant** : identifiant du centre `SM-001` · consultant `MG-01` · mot de passe `demo1234`.
- **Patient** : le scan est réel, il faut donc un QR code. Les QR de démonstration sont dans le dossier [`qr_demo/`](qr_demo/) :

  | Fichier | Contenu du QR | Résultat attendu |
  |---|---|---|
  | `qr-valide-saint-martin.png` | `mediqueue://queue/SM-MG-DEMO01` | Ticket créé : Centre Saint-Martin, Médecine générale |
  | `qr-jeton-inconnu.png` | `mediqueue://queue/INCONNU-999` | Message : « Ce QR code n'est pas reconnu par MediQueue. » |
  | `qr-externe-site-web.png` | `https://example.com` | Message : « Ce QR code n'est pas un QR code MediQueue. » |

  Afficher l'image sur l'écran d'un ordinateur et la scanner avec le téléphone.
- **Émulateur** : sans vraie caméra, charger l'image du QR dans *Extended controls → Camera → Virtual scene → Add image*, puis la placer devant la caméra virtuelle.

### Passer à l'API réelle

Dans chaque application, ouvrir `Config/AppConfig.cs` et remplacer `ServiceMode.Simulation` par `ServiceMode.Api` ; renseigner `LanHost` (adresse IPv4 du PC qui héberge le backend) si le test se fait sur un téléphone physique. Aucun autre fichier à modifier.

## 8. Historique Git par phase

| Phase | Contenu |
|---|---|
| **Phase A** | Socle des deux applications : projets, configuration, thème, modèles, services de simulation |
| **Phase B** | Interfaces de l'application Patient |
| **Phase C** | Interfaces de l'application Consultant |
| **Fonctionnalités ajoutées** | Scan QR par caméra · bottom sheets de confirmation · persistance locale · client API réel |

## 9. Périmètre de cette étape

Cette livraison contient le **frontend terminé de toutes les interfaces**, fonctionnel en mode simulation. Le backend (FastAPI + SQLite) sera réalisé ensuite : les applications sont déjà prêtes à s'y connecter.