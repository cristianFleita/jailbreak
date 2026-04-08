/**
 * SocketBridge.jslib
 * Bridges Unity C# ↔ socket.io in WebGL builds.
 *
 * C# calls jslib functions to send events.
 * JS calls unityInstance.SendMessage() to deliver events back to Unity.
 *
 * The target GameObject name is passed on SocketConnect and stored in
 * window._jbGoName so all callbacks know where to deliver.
 */

mergeInto(LibraryManager.library, {

  // ── Internal helpers ──────────────────────────────────────────────────────

  _jb_alloc: function(str) {
    var size = lengthBytesUTF8(str) + 1;
    var buf  = _malloc(size);
    stringToUTF8(str, buf, size);
    return buf;
  },

  _jb_send: function(method, payload) {
    if (!window.unityInstance) { console.warn('[SocketBridge] unityInstance not ready'); return; }
    var goName = window._jbGoName || 'NetworkManager';
    window.unityInstance.SendMessage(goName, method, payload || '');
  },

  // ── Connect ───────────────────────────────────────────────────────────────

  /**
   * SocketConnect(goName, displayName)
   * Loads socket.io from CDN if needed, then connects and authenticates.
   * goName = Unity GameObject that will receive SendMessage callbacks.
   */
  SocketConnect: function(goNamePtr, displayNamePtr) {
    var goName      = UTF8ToString(goNamePtr);
    var displayName = UTF8ToString(displayNamePtr);

    window._jbGoName      = goName;
    window._jbDisplayName = displayName;

    function doConnect() {
      if (window._jbSocket) {
        window._jbSocket.disconnect();
        window._jbSocket = null;
      }

      var url         = window.BACKEND_URL || 'http://localhost:3001';
      var savedUserId = localStorage.getItem('jailbreak_user_id') || null;

      console.log('[SocketBridge] Connecting to', url, '| savedUserId:', savedUserId);

      window._jbSocket = io(url, {
        transports: ['websocket', 'polling'],
        reconnection: true,
        reconnectionAttempts: 10,
        reconnectionDelay: 1500,
      });

      // ── Connection lifecycle ─────────────────────────────────────────────

      window._jbSocket.on('connect', function () {
        console.log('[SocketBridge] Connected:', window._jbSocket.id);
        // Authenticate immediately
        window._jbSocket.emit('auth:register', {
          userId: savedUserId,
          displayName: displayName,
        });
      });

      window._jbSocket.on('disconnect', function (reason) {
        console.log('[SocketBridge] Disconnected:', reason);
        window.unityInstance.SendMessage(window._jbGoName, 'OnSocketDisconnected', reason);
      });

      window._jbSocket.on('connect_error', function (err) {
        console.error('[SocketBridge] connect_error:', err.message);
        window.unityInstance.SendMessage(window._jbGoName, 'OnNetworkError',
          JSON.stringify({ message: err.message }));
      });

      // ── Auth ─────────────────────────────────────────────────────────────

      window._jbSocket.on('auth:registered', function (data) {
        console.log('[SocketBridge] Authenticated:', data.userId);
        localStorage.setItem('jailbreak_user_id',    data.userId);
        localStorage.setItem('jailbreak_display_name', data.displayName);
        window.JAILBREAK_USER_ID = data.userId;
        window.unityInstance.SendMessage(window._jbGoName, 'OnAuthRegistered', JSON.stringify(data));
      });

      // ── Room Lobby ───────────────────────────────────────────────────────

      window._jbSocket.on('room:created', function (data) {
        window.JAILBREAK_ROOM_ID = data.roomId;
        window.unityInstance.SendMessage(window._jbGoName, 'OnRoomCreated', JSON.stringify(data));
      });

      window._jbSocket.on('room:state', function (data) {
        window.JAILBREAK_ROOM_ID = data.roomId;
        window.unityInstance.SendMessage(window._jbGoName, 'OnRoomState', JSON.stringify(data));
      });

      window._jbSocket.on('room:player-joined', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnRoomPlayerJoined', JSON.stringify(data));
      });

      window._jbSocket.on('room:player-left', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnRoomPlayerLeft', JSON.stringify(data));
      });

      window._jbSocket.on('room:kicked', function (data) {
        window.JAILBREAK_ROOM_ID = '';
        window.unityInstance.SendMessage(window._jbGoName, 'OnRoomKicked', JSON.stringify(data));
      });

      window._jbSocket.on('room:destroyed', function (data) {
        window.JAILBREAK_ROOM_ID = '';
        window.unityInstance.SendMessage(window._jbGoName, 'OnRoomDestroyed', JSON.stringify(data));
      });

      // ── Gameplay ─────────────────────────────────────────────────────────

      window._jbSocket.on('game:start', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnGameStart', JSON.stringify(data));
      });

      window._jbSocket.on('game:end', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnGameEnd', JSON.stringify(data));
      });

      window._jbSocket.on('game:reconnect', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnGameReconnect', JSON.stringify(data));
      });

      window._jbSocket.on('player:state', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnPlayerState', JSON.stringify(data));
      });

      window._jbSocket.on('npc:positions', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnNPCPositions', JSON.stringify(data));
      });

      window._jbSocket.on('chase:start', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnChaseStart', JSON.stringify(data));
      });

      window._jbSocket.on('guard:catch', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnGuardCatchResult', JSON.stringify(data));
      });

      window._jbSocket.on('item:pickup', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnItemPickup', JSON.stringify(data));
      });

      window._jbSocket.on('riot:available', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnRiotAvailable', JSON.stringify(data));
      });

      window._jbSocket.on('game:error', function (data) {
        window.unityInstance.SendMessage(window._jbGoName, 'OnNetworkError', JSON.stringify(data));
      });

      window._jbSocket.on('phase:start', function (data) {                                                                                                     
        window.unityInstance.SendMessage(window._jbGoName, 'OnPhaseJailStart', JSON.stringify(data));                                                            
      });                                                                                                                                                        
      
      window._jbSocket.on('phase:warning', function (data) {                                                                                                     
        window.unityInstance.SendMessage(window._jbGoName, 'OnPhaseWarning', JSON.stringify(data));
      });                                                                                                                                                        
      
      window._jbSocket.on('npc:reassign', function (data) {                                                                                                      
        window.unityInstance.SendMessage(window._jbGoName, 'OnNPCReassign', JSON.stringify(data));
      });                                                                                                                                                        
      
      window._jbSocket.on('phase:zone_check', function (data) {                                                                                                  
        window.unityInstance.SendMessage(window._jbGoName, 'OnPhaseZoneCheck', JSON.stringify(data));
      });

    } // end doConnect

    // Load socket.io from CDN if the global `io` isn't available yet
    if (typeof io === 'undefined') {
      if (window._jbLoadingSocketIO) {
        // Already loading — wait for it
        document.addEventListener('jb_socketio_ready', doConnect, { once: true });
        return;
      }
      window._jbLoadingSocketIO = true;
      console.log('[SocketBridge] Loading socket.io from CDN...');
      var s = document.createElement('script');
      s.src = 'https://cdn.socket.io/4.8.0/socket.io.min.js';
      s.onload = function () {
        window._jbLoadingSocketIO = false;
        document.dispatchEvent(new Event('jb_socketio_ready'));
        doConnect();
      };
      s.onerror = function () {
        console.error('[SocketBridge] Failed to load socket.io from CDN');
        window.unityInstance.SendMessage(window._jbGoName, 'OnNetworkError',
          JSON.stringify({ message: 'Failed to load socket.io' }));
      };
      document.head.appendChild(s);
    } else {
      doConnect();
    }
  },

  // ── Emit helpers ──────────────────────────────────────────────────────────

  SocketCreateRoom: function(roomNamePtr) {
    var roomName = UTF8ToString(roomNamePtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('room:create', { roomName: roomName });
  },

  SocketJoinRoom: function(roomIdPtr) {
    var roomId = UTF8ToString(roomIdPtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('room:join', { roomId: roomId });
  },

  SocketKickPlayer: function(targetUserIdPtr) {
    var targetUserId = UTF8ToString(targetUserIdPtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('room:kick', { targetUserId: targetUserId });
  },

  SocketStartGame: function() {
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('room:start');
  },

  SocketLeaveRoom: function() {
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('room:leave');
  },

  SocketGetRoomState: function() {
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('room:get-state');
  },


  SocketSendPlayerMove: function(jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('player:move', JSON.parse(json));
  },

  SocketSendGuardMark: function(targetIdPtr) {
    var targetId = UTF8ToString(targetIdPtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('guard:mark', { targetId: targetId });
  },

  SocketSendGuardCatch: function(targetIdPtr) {
    var targetId = UTF8ToString(targetIdPtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('guard:catch', { targetId: targetId });
  },

  SocketSendInteract: function(objectIdPtr, actionPtr) {
    var objectId = UTF8ToString(objectIdPtr);
    var action   = UTF8ToString(actionPtr);
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('player:interact', { objectId: objectId, action: action });
  },

  SocketSendRiotActivate: function() {
    if (window._jbSocket && window._jbSocket.connected)
      window._jbSocket.emit('riot:activate');
  },

  SocketDisconnect: function() {
    if (window._jbSocket) {
      window._jbSocket.disconnect();
      window._jbSocket = null;
    }
  },

  // ── localStorage helpers ──────────────────────────────────────────────────

  SocketGetSavedUserId: function() {
    var val  = localStorage.getItem('jailbreak_user_id') || '';
    var size = lengthBytesUTF8(val) + 1;
    var buf  = _malloc(size);
    stringToUTF8(val, buf, size);
    return buf;
  },

  SocketGetSavedDisplayName: function() {
    var val  = localStorage.getItem('jailbreak_display_name') || '';
    var size = lengthBytesUTF8(val) + 1;
    var buf  = _malloc(size);
    stringToUTF8(val, buf, size);
    return buf;
  },

  SocketIsConnected: function() {
    return (window._jbSocket && window._jbSocket.connected) ? 1 : 0;
  },
});
