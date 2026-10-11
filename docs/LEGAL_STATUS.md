# Rechtsstatus

Jede Rechtsregel des Spiels trägt einen **Verifikationsstatus** (`Verification` in `Core/Legal/Norms.cs`), der dem Spieler bei jeder Beurteilung angezeigt wird. Erfundene Artikel gibt es nicht: ein Test prüft, dass jede zitierte Norm im lokal abgelegten Fedlex-Text tatsächlich existiert.

| Status | Anzahl Normen | Bedeutung |
|---|---|---|
| `FedlexVerified` | 40 | Artikelnummer und Inhalt gegen den konsolidierten Bundestext geprüft (Test `LegalTests`) |
| `CourtPractice` | 1 | Bundesgerichtspraxis, im Spiel vereinfacht |
| `GameRule` | 2 | Spielabstraktion ohne Anspruch auf juristische Genauigkeit |
| `CantonalNotVerified` | 1 | Kantonales Recht – **nicht** in der Fedlex-Datenbank, Details ungeprüft |

## Quellen (Fedlex, Stand 2026-10-10, SHA-256 in `docs/law/index.json`)
SR 101 BV · 311.0 StGB · 312.0 StPO · 314.1 OBG · 314.11 OBV (Bussenliste wird **zur Laufzeit aus dem Gesetzestext geparst**) · 364 ZAG (polizeilicher Zwang) · 741.01 SVG · 741.013 SKV · 741.11 VRV · 741.13 Alkoholgrenzwerte-Verordnung · 741.21 SSV · 741.51 VZV.

Die Texte liegen unter `Assets/StreamingAssets/Law` (Kopie von `docs/law`) und werden vom Spiel geladen.

## Nicht abgedeckt / offen
* **Kantonale Polizeigesetze** (26 Stück): nur Struktur und Zuständigkeitslogik, Inhalte unverifiziert → vor Release durch eine juristisch kundige Person prüfen.
* Zuständigkeit Gemeinde-/Kantonspolizei und Konkordate: Spielregel, aus echten Gemeindegrenzen abgeleitet, rechtlich vereinfacht.
* Das Spiel ist **keine Rechtsberatung**.
