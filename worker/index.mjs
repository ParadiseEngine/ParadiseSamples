export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === '/samples') {
      url.pathname = '/samples/';
      return Response.redirect(url.href, 308);
    }
    if (!url.pathname.startsWith('/samples/')) return new Response('Not found', { status: 404 });
    const origin = new URL(env.PAGES_ORIGIN);
    url.protocol = origin.protocol;
    url.host = origin.host;
    url.pathname = url.pathname.slice('/samples'.length);
    const response = await fetch(new Request(url, request), { redirect: 'manual' });
    const headers = new Headers(response.headers);
    const location = headers.get('location');
    if (location) {
      const target = new URL(location, url);
      if (target.origin === origin.origin) {
        target.host = new URL(request.url).host;
        target.protocol = new URL(request.url).protocol;
        target.pathname = `/samples${target.pathname}`;
        headers.set('location', target.href);
      }
    }
    return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
  }
};
