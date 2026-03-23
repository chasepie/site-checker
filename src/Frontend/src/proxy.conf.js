const { env } = require('process');

const ASPNETCORE_HTTPS_PORT = env.ASPNETCORE_HTTPS_PORT;
const ASPNETCORE_URLS = env.ASPNETCORE_URLS;

let target;
if (ASPNETCORE_HTTPS_PORT) {
  target = `https://localhost:${ASPNETCORE_HTTPS_PORT}`;
} else if (ASPNETCORE_URLS) {
  target = ASPNETCORE_URLS.split(';')[0];
} else {
  target = 'http://localhost:8080';
}

const PROXY_CONFIG = [
  {
    context: [
      "/api/",
      '/dataHub',
      '/scalar',
      '/openapi'
    ],
    target,
    secure: false,
    ws: true
  }
]

module.exports = PROXY_CONFIG;
