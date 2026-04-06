# Session Log
<!-- Claude Code appends to this file at session end -->
## Sessions

### 2026-04-05
- Backend: set `package.json` `"type": "module"` so `module: nodenext` + `verbatimModuleSyntax` treat sources as ESM; updated relative imports to `.js` extensions; added minimal `src/routes/game.ts` (was imported but missing) so `tsc --noEmit` passes.
