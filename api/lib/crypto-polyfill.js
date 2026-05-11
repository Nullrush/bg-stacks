'use strict';
// Polyfill for Node < 19: crypto.randomUUID()
// The Azure SDK bundle expects crypto as a global (packaging bug)
const crypto = require('node:crypto');
if (!crypto.randomUUID) {
  crypto.randomUUID = function() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = Math.random() * 16 | 0;
      const v = c === 'x' ? r : (r & 0x3 | 0x8);
      return v.toString(16);
    });
  };
}
// Set crypto as global for bundled Azure SDK code
global.crypto = crypto;
