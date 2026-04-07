mergeInto(LibraryManager.library, {

  /**
   * Returns the persistent userId from the React socket client.
   * Stored in window.JAILBREAK_USER_ID (set after auth:register).
   */
  GetUserId: function () {
    var val = (typeof window !== "undefined" && window.JAILBREAK_USER_ID)
      ? window.JAILBREAK_USER_ID
      : "";
    var size = lengthBytesUTF8(val) + 1;
    var buf  = _malloc(size);
    stringToUTF8(val, buf, size);
    return buf;
  },

  /**
   * Returns the current room ID (set when player creates/joins a room).
   */
  GetRoomId: function () {
    var val = (typeof window !== "undefined" && window.JAILBREAK_ROOM_ID)
      ? window.JAILBREAK_ROOM_ID
      : "";
    var size = lengthBytesUTF8(val) + 1;
    var buf  = _malloc(size);
    stringToUTF8(val, buf, size);
    return buf;
  },

  /**
   * Returns the current user status: idle, in-lobby, in-game, won, lost.
   */
  GetUserStatus: function () {
    var val = (typeof window !== "undefined" && window.JAILBREAK_USER_STATUS)
      ? window.JAILBREAK_USER_STATUS
      : "idle";
    var size = lengthBytesUTF8(val) + 1;
    var buf  = _malloc(size);
    stringToUTF8(val, buf, size);
    return buf;
  },

  /**
   * Checks if the socket client is connected and authenticated.
   * Returns 1 (true) or 0 (false).
   */
  IsSocketAuthenticated: function () {
    if (typeof window !== "undefined" && window.JAILBREAK_USER_ID && window.JAILBREAK_USER_ID.length > 0) {
      return 1;
    }
    return 0;
  }

});
