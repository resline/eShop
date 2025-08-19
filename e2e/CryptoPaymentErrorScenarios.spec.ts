import { test, expect, type Page } from '@playwright/test';

test.describe('Crypto Payment Error Scenarios', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Ready for a new adventure?' })).toBeVisible();
  });

  test('Handle payment creation failure', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);

    // Mock payment creation failure
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ 
          error: 'Invalid payment request',
          details: 'Amount must be greater than 0'
        })
      });
    });

    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();

    // Verify error handling
    await expect(page.getByText(/invalid payment request|amount must be greater/i)).toBeVisible();
    
    // Verify error boundary provides retry option
    await expect(page.getByRole('button', { name: /try again|retry/i })).toBeVisible();
  });

  test('Handle address generation failure', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);

    // Mock address generation failure
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ 
          error: 'Address generation service unavailable',
          details: 'Unable to generate payment address at this time'
        })
      });
    });

    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();

    // Verify error handling
    await expect(page.getByText(/address generation.*unavailable|unable to generate/i)).toBeVisible();
    
    // Verify specific error message for address generation
    await expect(page.getByText(/payment address.*this time/i)).toBeVisible();
  });

  test('Handle QR code generation failure', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);

    // Mock successful payment creation but QR generation failure
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 1,
          paymentId: 'test-payment-123',
          cryptoCurrency: 'BTC',
          paymentAddress: '1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa',
          requestedAmount: 0.001,
          status: 'Pending',
          createdAt: new Date().toISOString(),
          expiresAt: new Date(Date.now() + 30 * 60 * 1000).toISOString()
        })
      });
    });

    // Mock QR code generation failure
    await page.route('/api/crypto-payments/qr-code', (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'QR code generation failed' })
      });
    });

    // Select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();

    // Verify payment details are shown despite QR failure
    await expect(page.locator('.crypto-payment-display')).toBeVisible();
    await expect(page.getByLabel('Payment Address')).toBeVisible();

    // Verify QR code error handling
    await expect(page.getByText(/QR.*generation.*failed/i)).toBeVisible();
    
    // Verify retry button for QR code
    await expect(page.getByRole('button', { name: /retry.*qr|generate.*qr/i })).toBeVisible();
    
    // Test QR retry functionality
    await page.unroute('/api/crypto-payments/qr-code');
    await page.route('/api/crypto-payments/qr-code', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'image/png',
        body: Buffer.from('fake-qr-code-data')
      });
    });
    
    await page.getByRole('button', { name: /retry.*qr|generate.*qr/i }).click();
    
    // Verify QR code appears after retry
    await expect(page.locator('[data-testid="qr-code"]')).toBeVisible({ timeout: 5000 });
  });

  test('Handle clipboard API failure', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);
    await selectCryptocurrency(page, 'Bitcoin');

    // Mock clipboard API failure
    await page.evaluate(() => {
      Object.assign(navigator, {
        clipboard: {
          writeText: () => Promise.reject(new Error('Clipboard access denied'))
        }
      });
    });

    const copyButton = page.getByRole('button', { name: 'Copy' });
    await copyButton.click();

    // Verify error handling for clipboard failure
    await expect(page.getByText(/copy.*failed|clipboard.*denied/i)).toBeVisible();
    
    // Verify button becomes disabled temporarily
    await expect(copyButton).toBeDisabled();
    
    // Verify error state clears after timeout
    await expect(copyButton).toBeEnabled({ timeout: 5000 });
  });

  test('Handle payment status check failures', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);
    await selectCryptocurrency(page, 'Bitcoin');

    // Mock initial payment creation success
    await simulatePaymentCreation(page);

    // Mock status check failure
    await page.route('/api/crypto-payments/*/status', (route) => {
      route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ 
          error: 'Service temporarily unavailable',
          retryAfter: 30
        })
      });
    });

    // Try to check status
    await page.getByRole('button', { name: 'Check Status' }).click();

    // Verify error handling
    await expect(page.getByText(/service.*unavailable|temporarily unavailable/i)).toBeVisible();
    
    // Verify retry functionality
    await expect(page.getByRole('button', { name: /retry.*check|check.*status/i })).toBeVisible();
    
    // Test auto-retry behavior
    await expect(page.getByText(/auto.*retry|retrying/i)).toBeVisible({ timeout: 10000 });
  });

  test('Handle blockchain explorer failures', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);
    await selectCryptocurrency(page, 'Bitcoin');
    await simulatePaymentCreation(page);

    // Mock payment with transaction hash
    await page.route('/api/crypto-payments/*/status', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          status: 'Pending',
          transactionHash: '0x1234567890abcdef1234567890abcdef12345678',
          confirmations: 1
        })
      });
    });

    await page.getByRole('button', { name: 'Check Status' }).click();

    // Mock explorer URL failure
    await page.evaluate(() => {
      const originalOpen = window.open;
      window.open = () => {
        throw new Error('Popup blocked or explorer unavailable');
      };
    });

    // Try to view on explorer
    const explorerButton = page.getByRole('button', { name: /view.*explorer|blockchain.*explorer/i });
    if (await explorerButton.isVisible()) {
      await explorerButton.click();
      
      // Verify error handling (no error should crash the app)
      await expect(page.locator('.crypto-payment-display')).toBeVisible();
    }
  });

  test('Handle rate limiting errors', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);

    // Mock rate limiting response
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 429,
        contentType: 'application/json',
        headers: {
          'Retry-After': '60'
        },
        body: JSON.stringify({ 
          error: 'Too many requests',
          message: 'Rate limit exceeded. Please try again in 60 seconds.'
        })
      });
    });

    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();

    // Verify rate limiting error handling
    await expect(page.getByText(/rate limit.*exceeded|too many requests/i)).toBeVisible();
    await expect(page.getByText(/try again.*60.*seconds/i)).toBeVisible();
    
    // Verify retry button is disabled initially
    const retryButton = page.getByRole('button', { name: /try again|retry/i });
    await expect(retryButton).toBeDisabled();
  });

  test('Handle concurrent payment attempts', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);

    // Mock concurrent request conflict
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({ 
          error: 'Payment already exists',
          message: 'A payment for this order is already in progress.',
          existingPaymentId: 'existing-payment-123'
        })
      });
    });

    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();

    // Verify conflict handling
    await expect(page.getByText(/payment.*already.*exists|already.*in progress/i)).toBeVisible();
    
    // Verify option to view existing payment
    await expect(page.getByRole('button', { name: /view.*existing|continue.*payment/i })).toBeVisible();
  });

  test('Handle validation errors', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);

    // Mock validation error
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 422,
        contentType: 'application/json',
        body: JSON.stringify({ 
          error: 'Validation failed',
          details: {
            amount: ['Amount must be between 0.00001 and 1000'],
            currency: ['Unsupported cryptocurrency']
          }
        })
      });
    });

    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();

    // Verify validation error handling
    await expect(page.getByText(/validation.*failed/i)).toBeVisible();
    await expect(page.getByText(/amount must be between/i)).toBeVisible();
    await expect(page.getByText(/unsupported cryptocurrency/i)).toBeVisible();
  });

  test('Handle session timeout during payment', async ({ page }) => {
    // Add item to cart and navigate to checkout
    await addItemToCart(page);
    await navigateToCheckout(page);
    await selectCryptoPaymentMethod(page);
    await selectCryptocurrency(page, 'Bitcoin');
    await simulatePaymentCreation(page);

    // Mock session timeout
    await page.route('**/api/**', (route) => {
      route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ 
          error: 'Unauthorized',
          message: 'Session has expired. Please log in again.'
        })
      });
    });

    // Try to check payment status
    await page.getByRole('button', { name: 'Check Status' }).click();

    // Verify session timeout handling
    await expect(page.getByText(/session.*expired|unauthorized/i)).toBeVisible();
    await expect(page.getByText(/log in again/i)).toBeVisible();
    
    // Verify redirect to login or refresh option
    await expect(page.getByRole('button', { name: /log in|refresh|reload/i })).toBeVisible();
  });
});

// Helper functions (reused from main test file)
async function addItemToCart(page: Page) {
  await page.getByRole('link', { name: 'Adventurer GPS Watch' }).click();
  await page.getByRole('button', { name: 'Add to shopping bag' }).click();
  await expect(page.getByText('Item added to bag')).toBeVisible();
}

async function navigateToCheckout(page: Page) {
  await page.getByRole('link', { name: 'shopping bag' }).click();
  await expect(page.getByRole('heading', { name: 'Shopping bag' })).toBeVisible();
  await page.getByRole('button', { name: 'Checkout' }).click();
  await expect(page.getByRole('heading', { name: 'Checkout' })).toBeVisible();
}

async function selectCryptoPaymentMethod(page: Page) {
  await page.getByRole('button', { name: 'Cryptocurrency' }).click();
  await expect(page.locator('.payment-method.selected')).toContainText('Cryptocurrency');
}

async function selectCryptocurrency(page: Page, currency: string) {
  await page.getByRole('button', { name: currency }).click();
  await expect(page.locator('[data-testid="currency-selected"]')).toContainText(currency);
}

async function simulatePaymentCreation(page: Page) {
  await page.route('/api/crypto-payments', (route) => {
    route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: 1,
        paymentId: 'test-payment-123',
        cryptoCurrency: 'BTC',
        paymentAddress: '1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa',
        requestedAmount: 0.001,
        status: 'Pending',
        createdAt: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 30 * 60 * 1000).toISOString()
      })
    });
  });
}