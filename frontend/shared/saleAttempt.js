export function clonePayload(payload) {
  return JSON.parse(JSON.stringify(payload));
}

export function newSaleAttempt(payload) {
  return { key: crypto.randomUUID(), payload: clonePayload(payload), uncertain: false };
}

export function isUncertainSaleError(error) {
  return Boolean(error?.uncertain) || error?.status === 504 || error?.status === 502;
}

export function hasActiveSession(session) {
  return Boolean(session?.id);
}
