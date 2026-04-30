/**
 * VoiceBridge.jslib
 * Browser-side WebRTC voice for Unity WebGL.
 *
 * Socket.io is used only for signaling. Microphone audio flows peer-to-peer
 * through RTCPeerConnection and is spatialized with Web Audio.
 */

mergeInto(LibraryManager.library, {
  Voice_Init: function(jsonPtr) {
    var cfg = JSON.parse(UTF8ToString(jsonPtr));

    if (!window.JailbreakVoice) {
      window.JailbreakVoice = (function() {
        var defaultIceServers = [
          { urls: 'stun:stun.l.google.com:19302' },
          { urls: 'stun:stun.cloudflare.com:3478' }
        ];

        var state = {
          roomId: '',
          userId: '',
          range: 10,
          fullVolumeDistance: 2,
          occlusionMultiplier: 0.35,
          muted: false,
          deafened: false,
          pushToTalk: false,
          joined: false,
          socket: null,
          socketHandlers: null,
          audioContext: null,
          audioUnlockHandler: null,
          gestureListenersBound: false,
          localStream: null,
          localSource: null,
          localMeter: null,
          localSendGain: null,
          localSendDestination: null,
          localSendStream: null,
          localSendMeter: null,
          lastTrackEnabled: null,
          listenerPose: null,
          speakerPoses: {},
          peers: {}
        };

        function init(config) {
          dispose();

          state.roomId = config.roomId || window.JAILBREAK_ROOM_ID || '';
          state.userId = config.userId || window.JAILBREAK_USER_ID || '';
          state.range = typeof config.range === 'number' ? config.range : 10;
          state.fullVolumeDistance = typeof config.fullVolumeDistance === 'number'
            ? config.fullVolumeDistance
            : 2;
          state.occlusionMultiplier = typeof config.occlusionMultiplier === 'number'
            ? config.occlusionMultiplier
            : 0.35;

          if (!state.roomId || !state.userId) {
            console.warn('[Voice] Missing roomId/userId; cannot join voice.');
            return;
          }
          if (!window._jbSocket || !window._jbSocket.connected) {
            console.warn('[Voice] Socket.io is not connected; cannot join voice.');
            return;
          }
          if (typeof RTCPeerConnection === 'undefined') {
            console.warn('[Voice] RTCPeerConnection is unavailable in this browser.');
            return;
          }

          state.socket = window._jbSocket;
          bindSocket();
          ensureAudioContext();
          bindAudioUnlockGestures();

          ensureMicrophone()
            .catch(function(err) {
              console.warn('[Voice] Microphone unavailable; receive-only voice remains active.', err);
              state.localStream = null;
            })
            .then(function() {
              joinVoiceRoom();
            });
        }

        function ensureAudioContext() {
          if (state.audioContext) return state.audioContext;
          var AudioContextCtor = window.AudioContext || window.webkitAudioContext;
          if (!AudioContextCtor) {
            console.warn('[Voice] Web Audio API is unavailable.');
            return null;
          }
          state.audioContext = new AudioContextCtor();
          return state.audioContext;
        }

        function bindAudioUnlockGestures() {
          if (state.gestureListenersBound) return;

          state.audioUnlockHandler = function() {
            resumeAudioContext();
          };

          window.addEventListener('pointerdown', state.audioUnlockHandler, false);
          window.addEventListener('keydown', state.audioUnlockHandler, false);
          window.addEventListener('touchstart', state.audioUnlockHandler, false);
          state.gestureListenersBound = true;
        }

        function unbindAudioUnlockGestures() {
          if (!state.gestureListenersBound || !state.audioUnlockHandler) return;

          window.removeEventListener('pointerdown', state.audioUnlockHandler, false);
          window.removeEventListener('keydown', state.audioUnlockHandler, false);
          window.removeEventListener('touchstart', state.audioUnlockHandler, false);
          state.audioUnlockHandler = null;
          state.gestureListenersBound = false;
        }

        function resumeAudioContext() {
          var ctx = ensureAudioContext();
          if (ctx && ctx.state === 'suspended') {
            ctx.resume()
              .then(function() {
                console.log('[Voice] AudioContext resumed.');
              })
              .catch(function(err) {
                console.warn('[Voice] AudioContext resume failed:', err);
              });
          }
        }

        function ensureMicrophone() {
          if (state.localStream) return Promise.resolve(state.localStream);
          if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            return Promise.reject(new Error('getUserMedia is unavailable'));
          }

          return navigator.mediaDevices.getUserMedia({
            video: false,
            audio: {
              echoCancellation: true,
              noiseSuppression: true,
              autoGainControl: true
            }
          }).then(function(stream) {
            state.localStream = stream;
            attachLocalAudioGraph(stream);
            setTransmitEnabled(false);
            return stream;
          });
        }

        function attachLocalAudioGraph(stream) {
          var ctx = ensureAudioContext();
          if (!ctx || !stream) return;

          disconnectLocalAudioGraph();

          try {
            state.localSource = ctx.createMediaStreamSource(stream);
            state.localMeter = createAudioMeter(state.localSource);
            state.localSendGain = ctx.createGain();
            state.localSendGain.gain.value = 0;
            state.localSendDestination = ctx.createMediaStreamDestination();
            state.localSource.connect(state.localSendGain);
            state.localSendGain.connect(state.localSendDestination);
            state.localSendMeter = createAudioMeter(state.localSendGain);
            state.localSendStream = state.localSendDestination.stream;
          } catch (err) {
            console.warn('[Voice] Local audio graph failed:', err);
            disconnectLocalAudioGraph();
          }
        }

        function disconnectLocalAudioGraph() {
          try { if (state.localSource) state.localSource.disconnect(); } catch (_) {}
          try { if (state.localSendGain) state.localSendGain.disconnect(); } catch (_) {}
          try {
            if (state.localMeter && state.localMeter.analyser) state.localMeter.analyser.disconnect();
          } catch (_) {}
          try {
            if (state.localSendMeter && state.localSendMeter.analyser) state.localSendMeter.analyser.disconnect();
          } catch (_) {}
          if (state.localSendStream) {
            var tracks = state.localSendStream.getTracks();
            for (var i = 0; i < tracks.length; i++) tracks[i].stop();
          }

          state.localSource = null;
          state.localMeter = null;
          state.localSendGain = null;
          state.localSendDestination = null;
          state.localSendStream = null;
          state.localSendMeter = null;
        }

        function createAudioMeter(source) {
          if (!source || !state.audioContext || !state.audioContext.createAnalyser) return null;

          var analyser = state.audioContext.createAnalyser();
          analyser.fftSize = 512;
          analyser.smoothingTimeConstant = 0.2;
          source.connect(analyser);

          return {
            analyser: analyser,
            data: new Uint8Array(analyser.fftSize)
          };
        }

        function readAudioLevel(meter) {
          if (!meter || !meter.analyser || !meter.data) return 0;

          try {
            meter.analyser.getByteTimeDomainData(meter.data);
          } catch (_) {
            return 0;
          }

          var sum = 0;
          for (var i = 0; i < meter.data.length; i++) {
            var centered = (meter.data[i] - 128) / 128;
            sum += centered * centered;
          }

          var rms = Math.sqrt(sum / meter.data.length);
          return Math.round(rms * 10000) / 10000;
        }

        function bindSocket() {
          unbindSocket();

          state.socketHandlers = {
            peers: function(data) {
              var peers = data && data.peers ? data.peers : [];
              console.log('[Voice] Peers available:', peers.map(function(peer) { return peer.userId; }));
              for (var i = 0; i < peers.length; i++) {
                var peer = peers[i];
                if (!peer || !peer.userId || peer.userId === state.userId) continue;
                createPeer(peer.userId, true);
              }
            },
            signal: function(data) {
              if (!data || !data.fromUserId || data.fromUserId === state.userId) return;
              handleSignal(data.fromUserId, data.signal);
            },
            peerLeft: function(data) {
              if (!data || !data.userId) return;
              closePeer(data.userId);
            },
            voiceState: function(data) {
              if (!data || !data.userId || !state.speakerPoses[data.userId]) return;
              state.speakerPoses[data.userId].muted = !!data.muted;
              applyPeerSpatialization(data.userId);
            }
          };

          state.socket.on('voice:peers', state.socketHandlers.peers);
          state.socket.on('voice:signal', state.socketHandlers.signal);
          state.socket.on('voice:peer-left', state.socketHandlers.peerLeft);
          state.socket.on('voice:state', state.socketHandlers.voiceState);
        }

        function unbindSocket() {
          if (!state.socket || !state.socketHandlers || !state.socket.off) return;

          state.socket.off('voice:peers', state.socketHandlers.peers);
          state.socket.off('voice:signal', state.socketHandlers.signal);
          state.socket.off('voice:peer-left', state.socketHandlers.peerLeft);
          state.socket.off('voice:state', state.socketHandlers.voiceState);
          state.socketHandlers = null;
        }

        function joinVoiceRoom() {
          if (!state.socket || !state.socket.connected || state.joined) return;
          state.socket.emit('voice:join', {
            roomId: state.roomId,
            userId: state.userId
          });
          state.joined = true;
          console.log('[Voice] Joined voice room:', state.roomId);
        }

        function createPeer(userId, shouldOffer) {
          if (state.peers[userId]) return state.peers[userId];

          var iceServers = window.JAILBREAK_VOICE_ICE_SERVERS || defaultIceServers;
          var pc = new RTCPeerConnection({ iceServers: iceServers });
          console.log('[Voice] Creating peer:', userId, shouldOffer ? 'offerer' : 'answerer');
          var peer = {
            userId: userId,
            pc: pc,
            pendingCandidates: [],
            source: null,
            panner: null,
            gain: null,
            meter: null,
            remoteStream: null
          };
          state.peers[userId] = peer;

          if (state.localSendStream && state.localSendStream.getAudioTracks().length > 0) {
            var tracks = state.localSendStream.getAudioTracks();
            for (var i = 0; i < tracks.length; i++) {
              pc.addTrack(tracks[i], state.localSendStream);
            }
          } else if (pc.addTransceiver) {
            pc.addTransceiver('audio', { direction: 'recvonly' });
          }

          pc.onicecandidate = function(event) {
            if (!event.candidate) return;
            sendSignal(userId, { candidate: event.candidate });
          };

          pc.ontrack = function(event) {
            console.log('[Voice] Remote audio track received from:', userId);
            var stream = event.streams && event.streams[0]
              ? event.streams[0]
              : new MediaStream([event.track]);
            attachRemoteStream(userId, stream);
          };

          pc.onconnectionstatechange = function() {
            console.log('[Voice] Peer connection state:', userId, pc.connectionState);
            if (pc.connectionState === 'failed' || pc.connectionState === 'closed') {
              closePeer(userId);
            }
          };

          pc.oniceconnectionstatechange = function() {
            console.log('[Voice] ICE connection state:', userId, pc.iceConnectionState);
          };

          if (shouldOffer) {
            pc.createOffer({ offerToReceiveAudio: true })
              .then(function(offer) { return pc.setLocalDescription(offer); })
              .then(function() { sendSignal(userId, { sdp: pc.localDescription }); })
              .catch(function(err) { console.warn('[Voice] Offer failed:', err); });
          }

          return peer;
        }

        function handleSignal(userId, signal) {
          if (!signal) return;

          var peer = createPeer(userId, false);
          var pc = peer.pc;

          if (signal.sdp) {
            pc.setRemoteDescription(signal.sdp)
              .then(function() {
                flushPendingCandidates(peer);
                if (signal.sdp.type !== 'offer') return null;
                return pc.createAnswer()
                  .then(function(answer) { return pc.setLocalDescription(answer); })
                  .then(function() { sendSignal(userId, { sdp: pc.localDescription }); });
              })
              .catch(function(err) { console.warn('[Voice] SDP handling failed:', err); });
            return;
          }

          if (signal.candidate) {
            if (pc.remoteDescription && pc.remoteDescription.type) {
              pc.addIceCandidate(signal.candidate).catch(function(err) {
                console.warn('[Voice] ICE candidate failed:', err);
              });
            } else {
              peer.pendingCandidates.push(signal.candidate);
            }
          }
        }

        function flushPendingCandidates(peer) {
          while (peer.pendingCandidates.length > 0) {
            peer.pc.addIceCandidate(peer.pendingCandidates.shift()).catch(function(err) {
              console.warn('[Voice] Queued ICE candidate failed:', err);
            });
          }
        }

        function sendSignal(toUserId, signal) {
          if (!state.socket || !state.socket.connected) return;
          state.socket.emit('voice:signal', {
            toUserId: toUserId,
            fromUserId: state.userId,
            signal: signal
          });
        }

        function attachRemoteStream(userId, stream) {
          var ctx = ensureAudioContext();
          if (!ctx) return;
          resumeAudioContext();

          var peer = state.peers[userId] || createPeer(userId, false);
          if (peer.remoteStream === stream && peer.source) return;

          disconnectAudio(peer);
          peer.remoteStream = stream;
          peer.source = ctx.createMediaStreamSource(stream);
          peer.meter = createAudioMeter(peer.source);
          peer.gain = ctx.createGain();
          peer.gain.gain.value = 0;

          if (ctx.createPanner) {
            peer.panner = ctx.createPanner();
            peer.panner.panningModel = 'HRTF';
            peer.panner.distanceModel = 'linear';
            peer.panner.refDistance = state.fullVolumeDistance;
            peer.panner.maxDistance = state.range;
            peer.panner.rolloffFactor = 1;
            peer.source.connect(peer.panner);
            peer.panner.connect(peer.gain);
          } else {
            peer.source.connect(peer.gain);
          }
          peer.gain.connect(ctx.destination);

          applyPeerSpatialization(userId);
        }

        function disconnectAudio(peer) {
          try { if (peer.source) peer.source.disconnect(); } catch (_) {}
          try { if (peer.panner) peer.panner.disconnect(); } catch (_) {}
          try { if (peer.gain) peer.gain.disconnect(); } catch (_) {}
          try { if (peer.meter && peer.meter.analyser) peer.meter.analyser.disconnect(); } catch (_) {}
          peer.source = null;
          peer.panner = null;
          peer.gain = null;
          peer.meter = null;
        }

        function closePeer(userId) {
          var peer = state.peers[userId];
          if (!peer) return;
          disconnectAudio(peer);
          try { peer.pc.close(); } catch (_) {}
          delete state.peers[userId];
          delete state.speakerPoses[userId];
        }

        function setPushToTalk(payload) {
          state.pushToTalk = !!(payload && payload.active);
          console.log('[Voice] Push-to-talk:', state.pushToTalk ? 'on' : 'off');
          resumeAudioContext();
          setTransmitEnabled(state.pushToTalk && !state.muted);
        }

        function setLocalMuted(payload) {
          state.muted = !!(payload && payload.muted);
          setTransmitEnabled(state.pushToTalk && !state.muted);
          if (state.socket && state.socket.connected && state.joined) {
            state.socket.emit('voice:state', {
              userId: state.userId,
              muted: state.muted,
              deafened: state.deafened
            });
          }
        }

        function setTransmitEnabled(enabled) {
          var active = !!enabled;

          if (state.localStream) {
            var tracks = state.localStream.getAudioTracks();
            for (var i = 0; i < tracks.length; i++) {
              tracks[i].enabled = true;
            }
          }

          if (state.localSendGain) {
            if (state.localSendGain.gain.setTargetAtTime && state.audioContext) {
              state.localSendGain.gain.setTargetAtTime(active ? 1 : 0, state.audioContext.currentTime, 0.01);
            } else {
              state.localSendGain.gain.value = active ? 1 : 0;
            }
          }

          if (state.lastTrackEnabled !== active) {
            var count = state.localSendStream ? state.localSendStream.getAudioTracks().length : 0;
            state.lastTrackEnabled = active;
            console.log('[Voice] Local transmit enabled:', active, 'tracks:', count);
          }
        }

        function setListenerPose(pose) {
          if (!pose || !pose.position) return;
          state.listenerPose = pose;

          var ctx = ensureAudioContext();
          if (ctx && ctx.listener) {
            setAudioParam(ctx.listener.positionX, pose.position.x);
            setAudioParam(ctx.listener.positionY, pose.position.y);
            setAudioParam(ctx.listener.positionZ, pose.position.z);

            if (pose.forward && pose.up) {
              if (ctx.listener.forwardX) {
                setAudioParam(ctx.listener.forwardX, pose.forward.x);
                setAudioParam(ctx.listener.forwardY, pose.forward.y);
                setAudioParam(ctx.listener.forwardZ, pose.forward.z);
                setAudioParam(ctx.listener.upX, pose.up.x);
                setAudioParam(ctx.listener.upY, pose.up.y);
                setAudioParam(ctx.listener.upZ, pose.up.z);
              } else if (ctx.listener.setOrientation) {
                ctx.listener.setOrientation(
                  pose.forward.x, pose.forward.y, pose.forward.z,
                  pose.up.x, pose.up.y, pose.up.z
                );
              }
            }
          }

          applyAllSpatialization();
        }

        function setSpeakerPose(pose) {
          if (!pose || !pose.userId) return;
          state.speakerPoses[pose.userId] = pose;
          applyPeerSpatialization(pose.userId);
        }

        function applyAllSpatialization() {
          for (var userId in state.peers) {
            if (Object.prototype.hasOwnProperty.call(state.peers, userId)) {
              applyPeerSpatialization(userId);
            }
          }
        }

        function applyPeerSpatialization(userId) {
          var peer = state.peers[userId];
          var pose = state.speakerPoses[userId];
          if (!peer || !peer.gain || !pose || !state.listenerPose) return;

          if (peer.panner && pose.position) {
            setAudioParam(peer.panner.positionX, pose.position.x);
            setAudioParam(peer.panner.positionY, pose.position.y);
            setAudioParam(peer.panner.positionZ, pose.position.z);
          }

          var gain = computeGain(pose);
          if (peer.gain.gain.setTargetAtTime && state.audioContext) {
            peer.gain.gain.setTargetAtTime(gain, state.audioContext.currentTime, 0.035);
          } else {
            peer.gain.gain.value = gain;
          }
        }

        function computeGain(pose) {
          if (state.deafened || pose.alive === false || pose.captured === true || pose.muted === true) {
            return 0;
          }

          var lp = state.listenerPose.position;
          var sp = pose.position;
          var dx = sp.x - lp.x;
          var dy = sp.y - lp.y;
          var dz = sp.z - lp.z;
          var distance = Math.sqrt(dx * dx + dy * dy + dz * dz);
          var gain;

          if (distance <= state.fullVolumeDistance) {
            gain = 1;
          } else if (distance >= state.range) {
            gain = 0;
          } else {
            gain = (state.range - distance) / (state.range - state.fullVolumeDistance);
          }

          if (pose.occluded) gain *= state.occlusionMultiplier;
          if (gain < 0) gain = 0;
          if (gain > 1) gain = 1;
          return gain;
        }

        function setAudioParam(param, value) {
          if (!param) return;
          if (param.setTargetAtTime && state.audioContext) {
            param.setTargetAtTime(value, state.audioContext.currentTime, 0.02);
          } else {
            param.value = value;
          }
        }

        function dispose() {
          setTransmitEnabled(false);
          unbindAudioUnlockGestures();
          disconnectLocalAudioGraph();

          if (state.socket && state.socket.connected && state.joined) {
            state.socket.emit('voice:leave', { roomId: state.roomId, userId: state.userId });
          }

          unbindSocket();

          for (var userId in state.peers) {
            if (Object.prototype.hasOwnProperty.call(state.peers, userId)) {
              closePeer(userId);
            }
          }

          if (state.localStream) {
            var tracks = state.localStream.getTracks();
            for (var i = 0; i < tracks.length; i++) tracks[i].stop();
          }

          state.localStream = null;
          state.localSource = null;
          state.localMeter = null;
          state.localSendGain = null;
          state.localSendDestination = null;
          state.localSendStream = null;
          state.localSendMeter = null;
          state.joined = false;
          state.socket = null;
          state.listenerPose = null;
          state.speakerPoses = {};
          state.peers = {};
          state.pushToTalk = false;
          state.muted = false;
          state.deafened = false;
          state.lastTrackEnabled = null;
        }

        function debug() {
          var peers = {};
          var localLevel = readAudioLevel(state.localMeter);
          var localSendLevel = readAudioLevel(state.localSendMeter);
          for (var userId in state.peers) {
            if (!Object.prototype.hasOwnProperty.call(state.peers, userId)) continue;
            var peer = state.peers[userId];
            var remoteLevel = readAudioLevel(peer.meter);
            var gain = peer.gain ? peer.gain.gain.value : null;
            peers[userId] = {
              signalingState: peer.pc.signalingState,
              iceConnectionState: peer.pc.iceConnectionState,
              iceGatheringState: peer.pc.iceGatheringState,
              connectionState: peer.pc.connectionState,
              hasRemoteStream: !!peer.remoteStream,
              hasOutputNode: !!peer.gain,
              gain: gain,
              remoteLevel: remoteLevel,
              remoteSpeaking: remoteLevel > 0.01,
              audibleLevel: gain == null ? null : Math.round(remoteLevel * gain * 10000) / 10000
            };
          }

          var localTracks = [];
          if (state.localStream) {
            var tracks = state.localStream.getAudioTracks();
            for (var i = 0; i < tracks.length; i++) {
              localTracks.push({
                enabled: tracks[i].enabled,
                muted: tracks[i].muted,
                readyState: tracks[i].readyState,
                label: tracks[i].label
              });
            }
          }

          var sendTracks = [];
          if (state.localSendStream) {
            var outboundTracks = state.localSendStream.getAudioTracks();
            for (var j = 0; j < outboundTracks.length; j++) {
              sendTracks.push({
                enabled: outboundTracks[j].enabled,
                muted: outboundTracks[j].muted,
                readyState: outboundTracks[j].readyState,
                label: outboundTracks[j].label
              });
            }
          }

          return {
            roomId: state.roomId,
            userId: state.userId,
            joined: state.joined,
            muted: state.muted,
            deafened: state.deafened,
            pushToTalk: state.pushToTalk,
            localLevel: localLevel,
            localSpeaking: localLevel > 0.01,
            localSendLevel: localSendLevel,
            localSending: localSendLevel > 0.01,
            transmitGain: state.localSendGain ? state.localSendGain.gain.value : null,
            audioContextState: state.audioContext ? state.audioContext.state : 'none',
            hasListenerPose: !!state.listenerPose,
            speakerPoseCount: Object.keys(state.speakerPoses).length,
            localTracks: localTracks,
            sendTracks: sendTracks,
            peers: peers,
            iceServers: window.JAILBREAK_VOICE_ICE_SERVERS || defaultIceServers
          };
        }

        return {
          init: init,
          setPushToTalk: setPushToTalk,
          setLocalMuted: setLocalMuted,
          setListenerPose: setListenerPose,
          setSpeakerPose: setSpeakerPose,
          dispose: dispose,
          debug: debug
        };
      })();
    }

    window.JailbreakVoice.init(cfg);
  },

  Voice_SetPushToTalk: function(jsonPtr) {
    if (!window.JailbreakVoice) return;
    window.JailbreakVoice.setPushToTalk(JSON.parse(UTF8ToString(jsonPtr)));
  },

  Voice_SetLocalMuted: function(jsonPtr) {
    if (!window.JailbreakVoice) return;
    window.JailbreakVoice.setLocalMuted(JSON.parse(UTF8ToString(jsonPtr)));
  },

  Voice_SetListenerPose: function(jsonPtr) {
    if (!window.JailbreakVoice) return;
    window.JailbreakVoice.setListenerPose(JSON.parse(UTF8ToString(jsonPtr)));
  },

  Voice_SetSpeakerPose: function(jsonPtr) {
    if (!window.JailbreakVoice) return;
    window.JailbreakVoice.setSpeakerPose(JSON.parse(UTF8ToString(jsonPtr)));
  },

  Voice_Dispose: function() {
    if (window.JailbreakVoice) window.JailbreakVoice.dispose();
  },
});
