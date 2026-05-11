'use strict';
require('./lib/crypto-polyfill.js');
const { app } = require('@azure/functions');
const { getUserId, handleGet, handlePut } = require('./lib/handlers.js');

app.http('tags', {
  methods:   ['GET', 'PUT'],
  authLevel: 'anonymous',   // SWA route rules enforce authentication before the function runs
  route:     'tags',
  handler: async (request, _context) => {
    const userId = getUserId(request);
    if (!userId) return { status: 401, jsonBody: { error: 'unauthenticated' } };

    if (request.method === 'GET') {
      return handleGet(userId);
    }

    if (request.method === 'PUT') {
      let body;
      try { body = await request.json(); } catch { return { status: 400, jsonBody: { error: 'invalid JSON' } }; }
      const { tags, etag } = body || {};
      if (
        !tags || typeof tags !== 'object' || Array.isArray(tags) ||
        !Array.isArray(tags.want) || !Array.isArray(tags.played) ||
        !tags.want.every(id => typeof id === 'number') ||
        !tags.played.every(id => typeof id === 'number')
      ) {
        return { status: 400, jsonBody: { error: 'invalid body' } };
      }
      return handlePut(userId, tags, etag ?? null);
    }

    return { status: 405 };
  },
});
