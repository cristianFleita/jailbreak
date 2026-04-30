import { useEffect, useRef, useState } from "react";

const BUILD_PATH = "/unity-build/Build";
const LOADER_FILE = `${BUILD_PATH}/unity-build.loader.js`;
const DATA_FILE = `${BUILD_PATH}/unity-build.data.unityweb`;
const FRAMEWORK_FILE = `${BUILD_PATH}/unity-build.framework.js.unityweb`;
const WASM_FILE = `${BUILD_PATH}/unity-build.wasm.unityweb`;

declare global {
  interface Window {
    unityInstance: any;
    onUnityReady?: () => void;
    BACKEND_URL?: string;
    JAILBREAK_VOICE_ICE_SERVERS?: RTCIceServer[];
    createUnityInstance: (
      canvas: HTMLCanvasElement,
      config: object,
      onProgress?: (progress: number) => void
    ) => Promise<any>;
  }
}

function waitForCreateUnityInstance(timeoutMs = 15000): Promise<void> {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    const check = () => {
      if (typeof window.createUnityInstance === "function") {
        resolve();
        return;
      }
      if (Date.now() - start > timeoutMs) {
        reject(new Error("Timed out waiting for window.createUnityInstance"));
        return;
      }
      setTimeout(check, 50);
    };
    check();
  });
}

const DEFAULT_VOICE_STUN_URLS = [
  "stun:stun.l.google.com:19302",
  "stun:stun.cloudflare.com:3478",
];

function splitEnvList(value: string | undefined): string[] {
  return (value ?? "")
    .split(",")
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function isIceServer(value: unknown): value is RTCIceServer {
  if (!value || typeof value !== "object") return false;
  const urls = (value as RTCIceServer).urls;
  return typeof urls === "string" || Array.isArray(urls);
}

function parseIceServersJson(value: string | undefined): RTCIceServer[] | null {
  if (!value) return null;

  try {
    const parsed: unknown = JSON.parse(value);
    if (Array.isArray(parsed) && parsed.every(isIceServer)) {
      return parsed;
    }
    console.warn("[UnityEmbed] VITE_VOICE_ICE_SERVERS_JSON must be an array of RTCIceServer objects.");
  } catch (error) {
    console.warn("[UnityEmbed] Failed to parse VITE_VOICE_ICE_SERVERS_JSON:", error);
  }

  return null;
}

function buildVoiceIceServers(): RTCIceServer[] {
  const explicitServers = parseIceServersJson(import.meta.env.VITE_VOICE_ICE_SERVERS_JSON);
  if (explicitServers?.length) return explicitServers;

  const stunUrls = splitEnvList(import.meta.env.VITE_VOICE_STUN_URLS);
  const turnUrls = splitEnvList(import.meta.env.VITE_VOICE_TURN_URLS);
  const turnUsername = import.meta.env.VITE_VOICE_TURN_USERNAME;
  const turnCredential = import.meta.env.VITE_VOICE_TURN_CREDENTIAL;

  const iceServers: RTCIceServer[] = (stunUrls.length ? stunUrls : DEFAULT_VOICE_STUN_URLS)
    .map((url) => ({ urls: url }));

  if (turnUrls.length > 0) {
    if (turnUsername && turnCredential) {
      iceServers.push({
        urls: turnUrls.length === 1 ? turnUrls[0] : turnUrls,
        username: turnUsername,
        credential: turnCredential,
      });
    } else {
      console.warn("[UnityEmbed] TURN URLs configured without username/credential; ignoring TURN server.");
    }
  }

  return iceServers;
}

function configureUnityRuntimeGlobals(): void {
  window.BACKEND_URL = import.meta.env.VITE_BACKEND_URL ?? "http://localhost:3001";
  window.JAILBREAK_VOICE_ICE_SERVERS = buildVoiceIceServers();
}

interface UnityEmbedProps {
  width?: string | number;
  height?: string | number;
  className?: string;
  style?: React.CSSProperties;
}

export default function UnityEmbed({
  width = "100%",
  height = "100%",
  className,
  style,
}: UnityEmbedProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [progress, setProgress] = useState(0);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const handleVisibilityChange = () => {
      
      if (document.visibilityState === "visible" && window.unityInstance) {
        console.log("[UnityEmbed] Tab focused: Signaling Unity to resume.");
      }
    };
  
    document.addEventListener("visibilitychange", handleVisibilityChange);
    
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, [loaded]);

  useEffect(() => {
    let cancelled = false;

    async function loadUnity() {
      if (!canvasRef.current) return;

      configureUnityRuntimeGlobals();

      if (!document.querySelector(`script[src="${LOADER_FILE}"]`)) {
        await new Promise<void>((resolve, reject) => {
          const s = document.createElement("script");
          s.src = LOADER_FILE;
          s.onload = () => resolve();
          s.onerror = () => reject(new Error(`Failed to load: ${LOADER_FILE}`));
          document.body.appendChild(s);
        });
      }

      if (cancelled) return;

      try {
        await waitForCreateUnityInstance();
      } catch (e: any) {
        if (!cancelled) setError(e.message);
        return;
      }

      if (cancelled) return;

      try {
        const instance = await window.createUnityInstance(
          canvasRef.current,
          {
            dataUrl: DATA_FILE,
            frameworkUrl: FRAMEWORK_FILE,
            codeUrl: WASM_FILE,
            companyName: "DefaultCompany",
            productName: "JAILBREAK",
            productVersion: "0.1",
          },
          (p: number) => {
            if (!cancelled) setProgress(Math.round(p * 100));
          }
        );

        if (!cancelled) {
          window.unityInstance = instance;
          setLoaded(true);
          console.log("[UnityEmbed] Unity loaded ✓");
          if (typeof window.onUnityReady === "function") window.onUnityReady();
        }
      } catch (err: any) {
        if (!cancelled) {
          setError(err?.message ?? "Unity failed to load");
          console.error("[UnityEmbed] error:", err);
        }
      }
    }

    loadUnity();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div
      className={className}
      style={{
        position: "relative",
        width,
        height,
        background: "#1a2e1a",
        ...style,
      }}
    >
      {/* Loading overlay */}
      {!loaded && !error && (
        <div
          style={{
            position: "absolute",
            inset: 0,
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            color: "#f3e7ca",
            fontFamily: "'Bebas Neue', Impact, sans-serif",
            background: "radial-gradient(ellipse at 70% 30%, #312e22 85%, #171613 100%)",
            gap: 24,
            zIndex: 10,
            letterSpacing: 1,
            boxShadow: "inset 0 0 180px #13130e",
          }}
        >
          <div
            style={{
              fontSize: 54,
              userSelect: "none",
              filter: "drop-shadow(0 2px 10px #13130e88)",
              color: "#e1b758",
            }}
          >
            🗝️
          </div>
          <div
            style={{
              fontSize: 34,
              fontWeight: 900,
              textTransform: "uppercase",
              letterSpacing: 2,
              color: "#e7effa",
              textShadow: "2px 4px 0 #000, 0 1px 8px #000000ad",
            }}
          >
            PRISON BREAK
          </div>
          <div
            style={{
              fontSize: 17,
              color: "#e1b758",
              fontWeight: 600,
              letterSpacing: 1,
              textShadow: "0 1px 10px #222",
              marginBottom: 6,
            }}
          >
            Escaping confinement...
          </div>
          {/* Prison bars as visual loader */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 3,
              width: 210,
              height: 28,
              margin: "6px 0",
              background: "#23231e",
              borderRadius: 7,
              position: "relative",
              boxShadow: "0 2px 16px #0e0e0b9b",
              overflow: "hidden",
            }}
          >
            {[...Array(7)].map((_, i) => (
              <div
                key={i}
                style={{
                  width: 15,
                  height: "100%",
                  background:
                    i < Math.ceil(progress / (100 / 7))
                      ? "linear-gradient(180deg, #faa733 0%, #33331d 100%)"
                      : "#595641",
                  borderRadius: 3,
                  boxShadow:
                    i < Math.ceil(progress / (100 / 7))
                      ? "0 1px 4px #ffecbc"
                      : "none",
                  transition: "background 0.3s, box-shadow 0.3s",
                }}
              />
            ))}
            {/* Animated escapee icon */}
            <div
              style={{
                position: "absolute",
                left: `${(progress / 100) * 180 + 5}px`,
                top: 1,
                transition: "left 0.4s cubic-bezier(.36,1.01,.81,.97)",
                fontSize: 18,
                userSelect: "none",
                textShadow: "0 1px 2px #000",
              }}
            >
              🏃‍♂️
            </div>
          </div>
          <div style={{ fontSize: 14, color: "#bbb", textShadow: "0 1px 7px #1d170a85" }}>
            {progress}%
          </div>
        </div>
      )}

      {/* Error overlay */}
      {error && (
        <div
          style={{
            position: "absolute",
            inset: 0,
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            color: "#ff6b6b",
            fontFamily: "sans-serif",
            gap: 12,
            padding: 24,
            textAlign: "center",
            zIndex: 10,
          }}
        >
          <div style={{ fontSize: 40 }}>⚠️</div>
          <div style={{ fontSize: 18, fontWeight: 600 }}>
            Failed to load game
          </div>
          <div style={{ fontSize: 13, color: "#aaa", maxWidth: 440 }}>
            {error}
          </div>
          <div style={{ fontSize: 12, color: "#666" }}>
            Place your Unity build in <code>public/unity/Build/</code>
            <br />
            Expected:{" "}
            <code>
              unity-build.loader.js / .data / .framework.js / .wasm
            </code>
          </div>
        </div>
      )}

      {/* Unity canvas */}
      <canvas
        ref={canvasRef}
        id="unity-canvas"
        style={{
          width: "100%",
          height: "100%",
          display: "block",
          opacity: loaded ? 1 : 0,
          transition: "opacity 0.5s ease",
        }}
      />
    </div>
  );
}
