'use strict';
const { TableClient, RestError } = require('@azure/data-tables');

const TABLE_NAME = 'usertags';
const ROW_KEY    = 'tags';

function getDefaultClient() {
  const conn = process.env.AZURE_STORAGE_CONNECTION_STRING;
  if (!conn) throw new Error('AZURE_STORAGE_CONNECTION_STRING is not set');
  return TableClient.fromConnectionString(conn, TABLE_NAME);
}

function getUserId(request) {
  const header = request.headers.get('x-ms-client-principal');
  if (!header) return null;
  try {
    const decoded = JSON.parse(Buffer.from(header, 'base64').toString('utf-8'));
    return decoded.userId || null;
  } catch { return null; }
}

async function handleGet(userId, makeClient = getDefaultClient) {
  const client = makeClient();
  try {
    const entity = await client.getEntity(userId, ROW_KEY);
    return {
      status: 200,
      jsonBody: {
        tags: JSON.parse(entity.tagsJson),
        etag: entity.etag,
      },
    };
  } catch (err) {
    if (err?.statusCode === 404) return { status: 204 };
    throw err;
  }
}

async function handlePut(userId, tags, clientEtag, makeClient = getDefaultClient) {
  const client = makeClient();
  const entity = {
    partitionKey: userId,
    rowKey:       ROW_KEY,
    tagsJson:     JSON.stringify(tags),
  };
  try {
    if (clientEtag == null) {
      await client.createEntity(entity);
    } else {
      await client.updateEntity(entity, 'Replace', { etag: clientEtag });
    }
    const updated = await client.getEntity(userId, ROW_KEY);
    return { status: 200, jsonBody: { etag: updated.etag } };
  } catch (err) {
    if (err?.statusCode === 412 || err?.statusCode === 409) {
      return { status: 409, jsonBody: { error: 'conflict' } };
    }
    throw err;
  }
}

module.exports = { getUserId, handleGet, handlePut };
