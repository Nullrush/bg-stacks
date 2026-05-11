'use strict';
const { describe, it } = require('node:test');
const assert = require('node:assert/strict');

// Inline RestError mock (avoids requiring @azure/data-tables in test setup)
class RestError extends Error {
  constructor(msg, statusCode) {
    super(msg);
    this.statusCode = statusCode;
    this.name = 'RestError';
  }
}

// makeClient returns a factory () => mockTableClient
function makeClient({ existingEntity = null, postWriteEtag = 'W/"new"', updateShouldConflict = false, createShouldConflict = false } = {}) {
  let written = false;
  let writtenEntity = null;
  return () => ({
    async getEntity() {
      if (!existingEntity && !written) throw new RestError('not found', 404);
      if (written) return { ...writtenEntity, etag: postWriteEtag };
      return existingEntity;
    },
    async createEntity(entity) {
      if (createShouldConflict) throw new RestError('entity already exists', 409);
      writtenEntity = entity;
      written = true;
    },
    async updateEntity(entity, _mode, opts) {
      if (updateShouldConflict) throw new RestError('precondition failed', 412);
      writtenEntity = entity;
      written = true;
    },
    async createTable() {},
  });
}

const { getUserId, handleGet, handlePut } = require('./handlers.js');

describe('getUserId', () => {
  it('returns null when header is missing', () => {
    const req = { headers: { get: () => null } };
    assert.equal(getUserId(req), null);
  });

  it('decodes the base64 principal and returns userId', () => {
    const principal = { userId: 'uid-abc', userDetails: 'user@example.com', identityProvider: 'google' };
    const encoded = Buffer.from(JSON.stringify(principal)).toString('base64');
    const req = { headers: { get: () => encoded } };
    assert.equal(getUserId(req), 'uid-abc');
  });

  it('returns null on invalid base64', () => {
    const req = { headers: { get: () => 'not-valid-json-base64!!!' } };
    assert.equal(getUserId(req), null);
  });
});

describe('handleGet', () => {
  it('returns 204 when no entity exists', async () => {
    const res = await handleGet('user1', makeClient({ existingEntity: null }));
    assert.equal(res.status, 204);
  });

  it('returns 200 with tags and etag when entity exists', async () => {
    const entity = {
      tagsJson: JSON.stringify({ want: [418059], played: [12345] }),
      etag: 'W/"abc123"',
    };
    const res = await handleGet('user1', makeClient({ existingEntity: entity }));
    assert.equal(res.status, 200);
    assert.deepEqual(res.jsonBody.tags, { want: [418059], played: [12345] });
    assert.equal(res.jsonBody.etag, 'W/"abc123"');
  });
});

describe('handlePut', () => {
  it('uses createEntity on first write (null etag) and returns new etag', async () => {
    const client = makeClient({ existingEntity: null, postWriteEtag: 'W/"new123"' });
    const res = await handlePut('user1', { want: [1], played: [] }, null, client);
    assert.equal(res.status, 200);
    assert.equal(res.jsonBody.etag, 'W/"new123"');
  });

  it('uses conditional update when etag is provided', async () => {
    let capturedEtag;
    const client = () => ({
      async updateEntity(_entity, _mode, opts) { capturedEtag = opts.etag; },
      async getEntity()  { return { tagsJson: '{"want":[],"played":[]}', etag: 'W/"updated"' }; },
      async createTable() {},
    });
    const res = await handlePut('user1', { want: [1], played: [] }, 'W/"old"', client);
    assert.equal(res.status, 200);
    assert.equal(capturedEtag, 'W/"old"');
    assert.equal(res.jsonBody.etag, 'W/"updated"');
  });

  it('returns 409 when Azure rejects the etag (412)', async () => {
    const client = makeClient({ updateShouldConflict: true });
    const res = await handlePut('user1', { want: [1], played: [] }, 'W/"stale"', client);
    assert.equal(res.status, 409);
  });

  it('returns 409 when createEntity fails (entity already exists)', async () => {
    const client = makeClient({ createShouldConflict: true });
    const res = await handlePut('user1', { want: [1], played: [] }, null, client);
    assert.equal(res.status, 409);
    assert.deepEqual(res.jsonBody, { error: 'conflict' });
  });
});
