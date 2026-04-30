import { afterEach, describe, expect, it } from 'vitest'
import {
  cleanupVoiceRoom,
  joinVoiceRoom,
  leaveVoiceRoom,
  listVoicePeers,
  relayVoiceSignal,
  resetVoiceSignalingForTests,
  updateVoiceState,
} from '../voice-signaling.js'

interface EmitRecord {
  target: string
  event: string
  payload: unknown
}

class FakeSocket {
  public emitted: EmitRecord[] = []

  constructor(public id: string) {}

  emit(event: string, payload: unknown): void {
    this.emitted.push({ target: this.id, event, payload })
  }
}

function createFakeIo() {
  const emitted: EmitRecord[] = []
  const io = {
    to(target: string) {
      return {
        emit(event: string, payload: unknown) {
          emitted.push({ target, event, payload })
        },
      }
    },
  }
  return { io: io as any, emitted }
}

describe('voice signaling', () => {
  afterEach(() => resetVoiceSignalingForTests())

  it('returns existing room peers when a user joins voice', () => {
    const { io } = createFakeIo()
    const a = new FakeSocket('sock-a') as any
    const b = new FakeSocket('sock-b') as any

    joinVoiceRoom(io, a, 'room-1', 'user-a')
    const peers = joinVoiceRoom(io, b, 'room-1', 'user-b')

    expect(peers).toEqual([{ userId: 'user-a', muted: false, deafened: false }])
    expect(b.emitted.find((e: EmitRecord) => e.event === 'voice:peers')?.payload).toEqual({
      peers: [{ userId: 'user-a', muted: false, deafened: false }],
    })
    expect(listVoicePeers('room-1').map(p => p.userId)).toEqual(['user-a', 'user-b'])
  })

  it('relays WebRTC signals only to the addressed peer in the same voice room', () => {
    const { io, emitted } = createFakeIo()
    const a = new FakeSocket('sock-a') as any
    const b = new FakeSocket('sock-b') as any

    joinVoiceRoom(io, a, 'room-1', 'user-a')
    joinVoiceRoom(io, b, 'room-1', 'user-b')

    const ok = relayVoiceSignal(io, a, {
      toUserId: 'user-b',
      signal: { sdp: { type: 'offer', sdp: 'fake-sdp' } },
    })

    expect(ok).toBe(true)
    expect(emitted).toContainEqual({
      target: 'sock-b',
      event: 'voice:signal',
      payload: {
        toUserId: 'user-b',
        fromUserId: 'user-a',
        signal: { sdp: { type: 'offer', sdp: 'fake-sdp' } },
      },
    })
  })

  it('broadcasts mute state metadata without touching audio payloads', () => {
    const { io, emitted } = createFakeIo()
    const a = new FakeSocket('sock-a') as any
    const b = new FakeSocket('sock-b') as any

    joinVoiceRoom(io, a, 'room-1', 'user-a')
    joinVoiceRoom(io, b, 'room-1', 'user-b')

    expect(updateVoiceState(io, a, { muted: true })).toBe(true)
    expect(emitted.filter(e => e.event === 'voice:state')).toEqual([
      { target: 'sock-a', event: 'voice:state', payload: { userId: 'user-a', muted: true, deafened: false } },
      { target: 'sock-b', event: 'voice:state', payload: { userId: 'user-a', muted: true, deafened: false } },
    ])
  })

  it('notifies remaining peers when a user leaves voice', () => {
    const { io, emitted } = createFakeIo()
    const a = new FakeSocket('sock-a') as any
    const b = new FakeSocket('sock-b') as any

    joinVoiceRoom(io, a, 'room-1', 'user-a')
    joinVoiceRoom(io, b, 'room-1', 'user-b')

    expect(leaveVoiceRoom(io, a, 'left')).toBe(true)
    expect(emitted).toContainEqual({
      target: 'sock-b',
      event: 'voice:peer-left',
      payload: { userId: 'user-a', reason: 'left' },
    })
    expect(listVoicePeers('room-1').map(p => p.userId)).toEqual(['user-b'])
  })

  it('cleans a whole room at match end or room destroy', () => {
    const { io, emitted } = createFakeIo()
    const a = new FakeSocket('sock-a') as any
    const b = new FakeSocket('sock-b') as any

    joinVoiceRoom(io, a, 'room-1', 'user-a')
    joinVoiceRoom(io, b, 'room-1', 'user-b')
    cleanupVoiceRoom(io, 'room-1', 'game-ended')

    expect(listVoicePeers('room-1')).toEqual([])
    expect(emitted.filter(e => e.event === 'voice:peer-left')).toEqual([
      { target: 'sock-b', event: 'voice:peer-left', payload: { userId: 'user-a', reason: 'game-ended' } },
      { target: 'sock-a', event: 'voice:peer-left', payload: { userId: 'user-b', reason: 'game-ended' } },
    ])
  })
})
