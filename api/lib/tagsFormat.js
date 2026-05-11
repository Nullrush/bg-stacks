'use strict';

/**
 * Convert compact cloud format { want: [ids], played: [ids] }
 * to runtime lookup { [gameId]: { want?: true, played?: true } }.
 */
function cloudToRuntime(cloud) {
  const tags = {};
  for (const id of cloud.want || []) {
    tags[id] = Object.assign(tags[id] || {}, { want: true });
  }
  for (const id of cloud.played || []) {
    tags[id] = Object.assign(tags[id] || {}, { played: true });
  }
  return tags;
}

/**
 * Convert runtime lookup to compact cloud format.
 * Game IDs are stored as numbers (not strings).
 */
function runtimeToCloud(tags) {
  const want = [], played = [];
  for (const [id, t] of Object.entries(tags)) {
    const n = Number(id);
    if (!Number.isFinite(n) || n <= 0) continue;
    if (t.want)   want.push(n);
    if (t.played) played.push(n);
  }
  return { want, played };
}

/**
 * Union-merge two runtime tag objects.
 * Any tag present in either set is kept; no tags are removed.
 */
function mergeTags(a, b) {
  const result = {};
  for (const [id, t] of Object.entries(a)) {
    result[id] = { ...t };
  }
  for (const [id, t] of Object.entries(b)) {
    result[id] = Object.assign(result[id] || {}, t);
  }
  return result;
}

/**
 * Deep-equal check for two runtime tag objects.
 */
function tagsAreEqual(a, b) {
  const aKeys = Object.keys(a).sort();
  const bKeys = Object.keys(b).sort();
  if (aKeys.length !== bKeys.length) return false;
  return aKeys.every((k, i) => {
    if (k !== bKeys[i]) return false;
    const ta = a[k], tb = b[k];
    return !!ta.want === !!tb.want && !!ta.played === !!tb.played;
  });
}

module.exports = { cloudToRuntime, runtimeToCloud, mergeTags, tagsAreEqual };
