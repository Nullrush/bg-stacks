'use strict';
const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const { cloudToRuntime, runtimeToCloud, mergeTags, tagsAreEqual } = require('./tagsFormat.js');

describe('cloudToRuntime', () => {
  it('converts want/played arrays to lookup object', () => {
    const result = cloudToRuntime({ want: [1, 2], played: [2, 3] });
    assert.deepEqual(result, {
      1: { want: true },
      2: { want: true, played: true },
      3: { played: true },
    });
  });

  it('handles missing arrays gracefully', () => {
    assert.deepEqual(cloudToRuntime({}), {});
    assert.deepEqual(cloudToRuntime({ want: [1] }), { 1: { want: true } });
  });
});

describe('runtimeToCloud', () => {
  it('converts lookup object to want/played arrays', () => {
    const result = runtimeToCloud({ 1: { want: true }, 2: { played: true }, 3: { want: true, played: true } });
    assert.deepEqual(result.want.sort(), [1, 3].sort());
    assert.deepEqual(result.played.sort(), [2, 3].sort());
  });

  it('returns numeric ids (not strings)', () => {
    const result = runtimeToCloud({ 418059: { want: true } });
    assert.ok(result.want.every(id => typeof id === 'number'));
  });

  it('round-trips through cloudToRuntime', () => {
    const original = { want: [1, 2], played: [2, 3] };
    const result = runtimeToCloud(cloudToRuntime(original));
    assert.deepEqual(result.want.sort((a,b)=>a-b), original.want);
    assert.deepEqual(result.played.sort((a,b)=>a-b), original.played);
  });

  it('silently skips non-numeric ids', () => {
    const result = runtimeToCloud({ 'abc': { want: true }, 418059: { played: true } });
    assert.ok(!result.want.some(id => isNaN(id) || id === null));
    assert.deepEqual(result.played, [418059]);
  });
});

describe('mergeTags', () => {
  it('unions both sets — tags from either side are kept', () => {
    const local  = { 1: { want: true }, 2: { played: true } };
    const cloud  = { 2: { want: true }, 3: { played: true } };
    const merged = mergeTags(local, cloud);
    assert.deepEqual(merged[1], { want: true });
    assert.deepEqual(merged[2], { want: true, played: true });
    assert.deepEqual(merged[3], { played: true });
  });

  it('handles empty inputs', () => {
    assert.deepEqual(mergeTags({}, { 1: { want: true } }), { 1: { want: true } });
    assert.deepEqual(mergeTags({ 1: { want: true } }, {}), { 1: { want: true } });
  });
});

describe('tagsAreEqual', () => {
  it('returns true for identical tags', () => {
    assert.ok(tagsAreEqual({ 1: { want: true } }, { 1: { want: true } }));
  });

  it('returns false when counts differ', () => {
    assert.ok(!tagsAreEqual({ 1: { want: true } }, {}));
  });

  it('returns false when tag values differ for same game', () => {
    assert.ok(!tagsAreEqual({ 1: { want: true } }, { 1: { played: true } }));
  });

  it('returns true for empty vs empty', () => {
    assert.ok(tagsAreEqual({}, {}));
  });
});
