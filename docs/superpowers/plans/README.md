# VoxScribe — plans par vagues (2026-08-30)

Quatre plans d'implémentation, à exécuter **dans l'ordre**. Chacun est autonome et
laisse le dépôt vert (`cd windows && dotnet test VoxScribe.CrossPlatform.slnf`),
mais les vagues 2 et 3 consomment des types créés par la vague 1.

| Vague | Plan | Contenu |
|---|---|---|
| 1 | [wave1-quick-wins](2026-08-30-wave1-quick-wins.md) | Journal d'injection, annulation de la dernière dictée, ponctuation vocale FR/EN, suggestions de dictionnaire |
| 2 | [wave2-interface](2026-08-30-wave2-interface.md) | Surfaçage des erreurs (AppHealth), aperçu partiel dans la pill, tableau de bord, historique enrichi |
| 3 | [wave3-deferred-partial-correction](2026-08-30-wave3-deferred-partial-correction.md) | Correction différée du texte déjà tapé (backspace + retape, plafonnée) |
| 4 | [wave4-communications-ducking](2026-08-30-wave4-communications-ducking.md) | Atténuation native Windows des autres sons pendant la dictée |

## Dépendances entre vagues

La vague 1 pose deux fondations que les suivantes consomment sans les redéfinir :

- `VoxScribe.Core.InjectionJournal`, exposé comme `DictationEngine.Journal` —
  `BeginDictation()`, `Record(string)`, `InjectedText`, `Retract(int)`.
- `ITextInjector.BackspaceAsync(CancellationToken)` → `ValueTask<bool>` — **une**
  frappe de retour arrière. La boucle de comptage vit dans Core, parce que
  `VoxScribe.Platform.Windows` doit rester sans logique.

Piège associé, valable partout : `Retract` et le planificateur de la vague 3
comptent des **chars** UTF-16, alors que les frappes se comptent en **graphèmes**
(`StringInfo.LengthInTextElements`) — une frappe efface un emoji entier.

La vague 1 ajoute aussi `RawText` à `DictationResult` et `TranscriptRecord` ; la
vague 2 y ajoute `Cleaned` **après** (un paramètre positionnel optionnel ne
s'insère jamais au milieu).

La vague 4 est indépendante des trois autres et peut être faite à tout moment.

## Ce que la CI ne peut pas prouver

Les vagues 1, 3 et 4 touchent l'injection réelle ou la capture audio. Chacune se
termine par une checklist de test manuel — c'est la règle du dépôt pour tout ce
qui passe par `PushToTalkHook`, `SendInput` ou WASAPI.

## État d'avancement (2026-09-04)

- **Vague 1 — partiellement livrée** : journal d'injection (`InjectionJournal`,
  `DictationEngine.Journal`) et annulation de la dernière dictée
  (`UndoLastDictationAsync` + touche UNDO, `ITextInjector.BackspaceAsync`) sont en
  place. Restent : ponctuation vocale (tâches 5–6) et suggestions de dictionnaire
  (tâches 7–9).
- Vagues 2–4 : non commencées.
- Hors plan, livrés le même jour : notices d'échec réseau (crash log + pill),
  clés API protégées DPAPI, tests `StreamingSegmenter` (avec correction d'un bug
  de contrat sur `Accept`), `HttpClient` partagé, gate `dotnet format` + Dependabot.

## Idées futures (non planifiées)

Candidates pour une vague ultérieure, à trier :

| Idée | Contenu | Note |
|---|---|---|
| Profils par application | Comportement par app ciblée (raccourci, cleanup, incrémental) — l'ancrage de focus identifie déjà la fenêtre cible | S'appuie sur `IFocusAnchor` |
| Recherche dans l'historique | Champ de recherche dans TranscriptionsView sur `TranscriptStore.Search` | Petit |
| Latence visible | Afficher `ProcessingTime` dans la pill à la fin d'une dictée — la latence du cleanup n'a jamais été mesurée sur vrai matériel (AGENTS.md) | Petit, forte valeur diagnostique |

## Hors vagues

| Plan | Contenu |
|---|---|
| [focus-anchor-and-settings](2026-09-04-focus-anchor-and-settings.md) | Ancrage du champ ciblé à l'appui (texte tapé dans ce champ à la relâche, où que soit l'utilisateur) ; refonte de la fenêtre Réglages (redimensionnable, défilante, un fichier par section). Indépendant des vagues 1–4. Spec : `docs/superpowers/specs/2026-09-04-focus-anchor-and-settings-design.md`. |
