// Launches an installed Chromium-based browser for the build helpers. Set BROWSER_CHANNEL to pick one
// (msedge, chrome); otherwise Edge is tried first, then Chrome.
import { chromium } from 'playwright-core';

export async function launch() {
    const channels = process.env.BROWSER_CHANNEL ? [process.env.BROWSER_CHANNEL] : ['msedge', 'chrome'];
    for (const channel of channels) {
        try {
            return await chromium.launch({ channel, headless: true });
        } catch {
            // Try the next one.
        }
    }
    throw new Error(`No browser found (tried ${channels.join(', ')}). Install Edge or Chrome, or set BROWSER_CHANNEL.`);
}

export const labs = ['routing', 'knapsack', 'job-shop', 'bin-packing', 'max-flow', 'matching'];
