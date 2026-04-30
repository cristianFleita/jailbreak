# JAILBREAK Web Wrapper

React + Vite wrapper for the Unity WebGL build. Vercel deploys this app and
Render deploys the Socket.io backend.

## Environment

Set these in Vercel Project Settings → Environment Variables:

```bash
VITE_BACKEND_URL=https://your-render-service.onrender.com

VITE_VOICE_STUN_URLS=stun:stun.l.google.com:19302,stun:stun.cloudflare.com:3478
VITE_VOICE_TURN_URLS=turns:your-turn-host.example.com:5349
VITE_VOICE_TURN_USERNAME=your-turn-user
VITE_VOICE_TURN_CREDENTIAL=your-turn-password
```

`VITE_BACKEND_URL` is exposed to Unity as `window.BACKEND_URL`.
Voice ICE servers are exposed as `window.JAILBREAK_VOICE_ICE_SERVERS` before
the Unity loader runs.

For local development, copy `.env.example` to `.env.local` and keep:

```bash
VITE_BACKEND_URL=http://localhost:3001
```

TURN is strongly recommended for public playtests. STUN-only WebRTC works for
many connections, but some NAT/firewall combinations need a relay.

Alternative single-variable override:

```bash
VITE_VOICE_ICE_SERVERS_JSON=[{"urls":"stun:stun.l.google.com:19302"},{"urls":"turns:your-turn-host.example.com:5349","username":"your-turn-user","credential":"your-turn-password"}]
```

If `VITE_VOICE_ICE_SERVERS_JSON` is set and valid, it replaces the STUN/TURN
variables above.

## Render Backend

Set this in Render so Socket.io accepts the Vercel origin:

```bash
CLIENT_URL=https://your-vercel-app.vercel.app
```

No TURN variables are required on Render for the current MVP because the
backend only relays signaling; actual audio flows peer-to-peer or through TURN
from the browser.

## Voice Troubleshooting

`[Voice] Joined voice room` only means the browser joined Socket.io signaling.
It does not guarantee that WebRTC connected or that the voice Web Audio mixer is
running.

In each browser, open DevTools and check:

```js
window.JailbreakVoice?.debug()
```

Healthy two-player state while both players are alive and near each other:

- `joined: true`
- `audioContextState: "running"`
- one entry in `peers`
- peer `connectionState: "connected"` or `"connecting"` briefly
- peer `iceConnectionState: "connected"` or `"completed"`
- `hasRemoteStream: true`
- `speakerPoseCount >= 1`
- while holding Space on the speaking client, `pushToTalk: true`,
  peer `transmitting: true`, `transmitGain > 0`, and `localSendLevel > 0.01`
- while speaking, the sender should show `localLevel > 0.01`
- the listener's peer entry should show `remoteLevel > 0.01` and
  `audibleLevel > 0.01`

If `audioContextState` stays `"suspended"`, click inside the game canvas or
press any gameplay key once in that browser. If ICE stays failed/disconnected,
configure TURN in Vercel and redeploy the web app.

`localLevel` measures microphone waveform before push-to-talk. `localSendLevel`
measures the intended outgoing waveform after push-to-talk gain. `remoteLevel`
measures the stable received WebRTC waveform before PTT/proximity gain.
`audibleLevel` is the received waveform after PTT/proximity attenuation.
