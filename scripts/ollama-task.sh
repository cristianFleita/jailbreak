#!/bin/bash
# Usage: bash scripts/ollama-task.sh "write tests for src/auth.ts"
MODEL="gemma4:e4b"
PROMPT="$1"
ollama run $MODEL "$PROMPT" --nowordwrap


