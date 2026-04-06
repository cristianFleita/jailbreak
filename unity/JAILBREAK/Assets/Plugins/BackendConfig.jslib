mergeInto(LibraryManager.library, {
  GetBackendUrl: function () {
    var url = (typeof window !== "undefined" && window.BACKEND_URL)
      ? window.BACKEND_URL
      : "http://localhost:3001";
    var size = lengthBytesUTF8(url) + 1;
    var buf  = _malloc(size);
    stringToUTF8(url, buf, size);
    return buf;
  }
});
