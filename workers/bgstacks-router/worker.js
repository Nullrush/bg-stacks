const ORIGIN = 'ca-bgstacks-prod.gentlefield-a3490518.centralus.azurecontainerapps.io';

export default {
  async fetch(request) {
    const url = new URL(request.url);

    const originUrl = new URL(request.url);
    originUrl.hostname = ORIGIN;

    const headers = new Headers(request.headers);
    headers.set('X-Event-Host', url.hostname);

    return fetch(new Request(originUrl.toString(), {
      method: request.method,
      headers,
      body: request.body,
      redirect: 'manual',
    }));
  },
};
