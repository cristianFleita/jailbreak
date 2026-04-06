# Test Suite Documentation

## Running Tests

### All Tests
```bash
npm run test
```

### Watch Mode (auto-rerun on file changes)
```bash
npm run test:watch
```

### UI Mode (visual test runner)
```bash
npm run test:ui
```

### Specific Test File
```bash
npx vitest run src/game/__tests__/state.test.ts
```

### With Coverage Report
```bash
npx vitest run --coverage
```

## Test Structure

```
backend/src/
├── game/
│   └── __tests__/
│       ├── state.test.ts              (13 unit tests)
│       ├── validation.test.ts         (10 unit tests)
│       ├── room-manager.test.ts       (8 unit tests)
│       ├── game-loop.integration.test.ts (6 integration tests)
│       └── systems/
│           └── __tests__/
│               └── game-manager.test.ts (8 unit tests)
│
└── __tests__/
    ├── socket-events.integration.test.ts (10 integration tests)
    └── performance.test.ts               (5 performance tests)
```

## Test Coverage

### Phase 1: State Management
- ✅ 31 unit tests covering state mutations, validation, room lifecycle
- ✅ Validates: adding players, spawning NPCs, delta compression, phase transitions

### Phase 2: Socket Events
- ✅ 10 integration tests covering event validation and broadcasts
- ✅ Validates: movement checks, item pickup race conditions, guard catch, chase initiation

### Phase 3: Game Systems
- ✅ 8 tests for GameManager
- ✅ Tests: event callbacks, stats tracking, system coordination

### Performance
- ✅ 5 tests for bandwidth, tick precision, memory, CPU load
- ✅ Validates: <5 KB/s bandwidth, <30ms tick variance, stable memory

## Test Categories

### Unit Tests (State, Validation, Room Manager)
Run in isolation; test single functions/modules.

```bash
npm run test -- src/game/__tests__/state.test.ts
npm run test -- src/game/__tests__/validation.test.ts
npm run test -- src/game/__tests__/room-manager.test.ts
```

### Integration Tests (Game Loop, Socket Events)
Test interactions between multiple systems (mocked Socket.io).

```bash
npm run test -- src/game/__tests__/game-loop.integration.test.ts
npm run test -- src/__tests__/socket-events.integration.test.ts
```

### Performance Tests
Measure bandwidth, timing, memory, CPU over realistic scenarios.

```bash
npm run test -- src/__tests__/performance.test.ts
```

## Expected Test Results

All tests should pass with:
- ✅ 60 tests passing
- ✅ 0 tests failing
- ✅ Coverage >80% for src/game/

Example output:
```
✓ src/game/__tests__/state.test.ts (13)
✓ src/game/__tests__/validation.test.ts (10)
✓ src/game/__tests__/room-manager.test.ts (8)
✓ src/game/__tests__/game-loop.integration.test.ts (6)
✓ src/__tests__/socket-events.integration.test.ts (10)
✓ src/__tests__/performance.test.ts (5)
✓ src/game/systems/__tests__/game-manager.test.ts (8)

Test Files  7 passed (7)
Tests      60 passed (60)
Duration   3.5s
```

## Debugging Tests

### Run a Single Test
```bash
npx vitest run src/game/__tests__/state.test.ts -t "should add first player as guard"
```

### Show Console Output
```bash
npx vitest run --reporter=verbose
```

### Watch a Specific File
```bash
npx vitest watch src/game/__tests__/validation.test.ts
```

## Common Issues

### Tests hang / timeout
- Check that `stopGameLoop()` is called in `afterEach`
- Ensure mock Socket.io is not broadcasting forever

### Import errors
- Ensure all paths use `.js` extensions (ESM)
- Check `tsconfig.json` has `"moduleResolution": "nodenext"`

### Memory tests flaky
- Memory measurements are approximates; allow 10-20MB tolerance
- Run in isolation if needed: `--run --no-coverage`

## CI/CD Integration

Tests are configured to run in Node environment (not browser).
For GitHub Actions or similar:

```yaml
- name: Run Tests
  run: |
    cd backend
    npm install
    npm run test
```

## Coverage Goals

Target >80% coverage for src/game/

Current coverage measured by:
```bash
npm run test -- --coverage
```

Coverage report generated in `coverage/index.html`
