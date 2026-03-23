import { type AddressInfo, createServer } from 'net';
import { chromium } from 'playwright';

function findFreePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = createServer();
    server.listen(0, () => {
      const { port } = server.address() as AddressInfo;
      server.close(() => resolve(port));
    });
    server.on('error', reject);
  });
}

async function launchServer() {
  type LaunchOptions = Parameters<typeof chromium.launchServer>[0] & { cdpPort: number };

  const options = {
    headless: false,
    ignoreDefaultArgs: [
      '--enable-automation',
    ],
    port: 3123,
    wsPath: 'playwright',
    cdpPort: await findFreePort(),
  } satisfies LaunchOptions;

  return await chromium.launchServer(options);
}

async function waitForExit() {
  return new Promise<void>((resolve) => {
    process.on('SIGINT', () => resolve());
    process.on('SIGTERM', () => resolve());
    process.on('exit', () => resolve());
  });
}

async function main() {
  const server = await launchServer();
  console.log(`Browser server launched at wsEndpoint: ${server.wsEndpoint()}`);

  await waitForExit();
  await server.close();
  console.log('Browser server closed.');
}

main()
  .catch(err => {
    console.error(err);
    process.exit(1);
  })
  .then(() => {
    process.exit(0);
  });
