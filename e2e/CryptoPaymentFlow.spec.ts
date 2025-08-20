import { test, expect, type Page } from '@playwright/test';

test.describe('Crypto Payment Flow', () => {
  test.beforeEach(async ({ page }) => {
    // Set up test data
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Ready for a new adventure?' })).toBeVisible();
  });

  test('Complete crypto payment flow - Bitcoin', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Bitcoin
    await selectCryptocurrency(page, 'Bitcoin');
    
    // Verify payment details are displayed
    await verifyPaymentDetails(page, 'BTC');
    
    // Verify QR code is generated
    await verifyQRCodeGeneration(page);
    
    // Verify payment address is copyable
    await verifyAddressCopy(page);
    
    // Simulate payment confirmation
    await simulatePaymentConfirmation(page);
    
    // Verify order completion
    await verifyOrderCompletion(page);
  });

  test('Complete crypto payment flow - Ethereum', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Ethereum
    await selectCryptocurrency(page, 'Ethereum');
    
    // Verify payment details are displayed
    await verifyPaymentDetails(page, 'ETH');
    
    // Verify QR code is generated
    await verifyQRCodeGeneration(page);
    
    // Verify payment address is copyable
    await verifyAddressCopy(page);
    
    // Simulate payment confirmation
    await simulatePaymentConfirmation(page);
    
    // Verify order completion
    await verifyOrderCompletion(page);
  });

  test('Handle crypto payment timeout', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Bitcoin
    await selectCryptocurrency(page, 'Bitcoin');
    
    // Wait for payment to timeout (mock fast timeout for testing)
    await page.locator('[data-testid="payment-timer"]').waitFor({ timeout: 30000 });
    
    // Verify timeout message is displayed
    await expect(page.getByText('Payment window has expired')).toBeVisible();
    
    // Verify retry option is available
    await expect(page.getByRole('button', { name: 'Try Again' })).toBeVisible();
    
    // Test retry functionality
    await page.getByRole('button', { name: 'Try Again' }).click();
    
    // Verify new payment details are generated
    await verifyPaymentDetails(page, 'BTC');
  });

  test('Handle network connectivity issues', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Simulate network failure
    await page.context().setOffline(true);
    
    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();
    
    // Verify error handling
    await expect(page.getByText(/network error|connection/i)).toBeVisible();
    
    // Restore network
    await page.context().setOffline(false);
    
    // Verify retry functionality
    await page.getByRole('button', { name: /retry|try again/i }).click();
    
    // Verify payment setup works after network restoration
    await verifyPaymentDetails(page, 'BTC');
  });

  test('Verify payment status updates', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Bitcoin
    await selectCryptocurrency(page, 'Bitcoin');
    
    // Verify initial pending status
    await expect(page.getByText('Transaction Pending')).toBeVisible();
    await expect(page.getByText('Waiting for network confirmations')).toBeVisible();
    
    // Mock transaction detection
    await mockTransactionDetection(page);
    
    // Verify status updates
    await expect(page.getByText('Transaction Detected')).toBeVisible();
    
    // Mock confirmations
    await mockConfirmationProgress(page);
    
    // Verify confirmation progress
    await expect(page.getByText(/confirmations:/i)).toBeVisible();
    
    // Mock final confirmation
    await mockFinalConfirmation(page);
    
    // Verify payment confirmed
    await expect(page.getByText('Payment Confirmed')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Continue to Order' })).toBeVisible();
  });

  test('Test mobile wallet integration', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Bitcoin
    await selectCryptocurrency(page, 'Bitcoin');
    
    // Verify mobile-specific elements
    await expect(page.getByRole('button', { name: 'Open Wallet App' })).toBeVisible();
    
    // Test wallet app integration
    const walletButton = page.getByRole('button', { name: 'Open Wallet App' });
    
    // Mock wallet app opening (can't actually test external app)
    await walletButton.click();
    
    // Verify no errors occurred
    await expect(page.locator('[data-testid="error-message"]')).not.toBeVisible();
  });

  test('Test accessibility features', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Bitcoin
    await selectCryptocurrency(page, 'Bitcoin');
    
    // Test keyboard navigation
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(page.locator(':focus')).toBeVisible();
    
    // Test screen reader labels
    await expect(page.getByLabel('Payment Address')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Copy payment address' })).toBeVisible();
    
    // Test high contrast mode compatibility
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(page.locator('.crypto-payment-display')).toBeVisible();
  });

  test('Test payment amount validation', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Choose Bitcoin
    await selectCryptocurrency(page, 'Bitcoin');
    
    // Verify amount display
    const cryptoAmount = page.locator('[data-testid="crypto-amount"]');
    await expect(cryptoAmount).toBeVisible();
    
    // Verify USD equivalent
    const usdEquivalent = page.locator('[data-testid="usd-equivalent"]');
    await expect(usdEquivalent).toBeVisible();
    await expect(usdEquivalent).toContainText('$');
    
    // Verify precision (should show 8 decimal places for Bitcoin)
    const amountText = await cryptoAmount.textContent();
    expect(amountText).toMatch(/\d+\.\d{8}\s+BTC/);
  });

  test('Test error boundary functionality', async ({ page }) => {
    // Add item to cart
    await addItemToCart(page);
    
    // Navigate to checkout
    await navigateToCheckout(page);
    
    // Mock API error
    await page.route('/api/crypto-payments', (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Internal server error' })
      });
    });
    
    // Select cryptocurrency payment method
    await selectCryptoPaymentMethod(page);
    
    // Try to select cryptocurrency
    await page.getByRole('button', { name: 'Bitcoin' }).click();
    
    // Verify error boundary catches the error
    await expect(page.getByText(/error occurred|something went wrong/i)).toBeVisible();
    
    // Verify retry functionality
    await expect(page.getByRole('button', { name: /try again|retry/i })).toBeVisible();
    
    // Remove mock to test recovery
    await page.unroute('/api/crypto-payments');
    
    // Test retry
    await page.getByRole('button', { name: /try again|retry/i }).click();
    
    // Verify recovery
    await verifyPaymentDetails(page, 'BTC');
  });
});

// Helper functions
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

async function verifyPaymentDetails(page: Page, symbol: string) {
  // Verify payment display is visible
  await expect(page.locator('.crypto-payment-display')).toBeVisible();
  
  // Verify currency symbol
  await expect(page.getByText(symbol)).toBeVisible();
  
  // Verify payment address
  await expect(page.getByLabel('Payment Address')).toBeVisible();
  const addressInput = page.getByLabel('Payment Address');
  const address = await addressInput.inputValue();
  expect(address).toBeTruthy();
  expect(address.length).toBeGreaterThan(20);
  
  // Verify timer
  await expect(page.locator('[data-testid="payment-timer"]')).toBeVisible();
  
  // Verify instructions
  await expect(page.getByText('Copy the address or scan the QR code')).toBeVisible();
}

async function verifyQRCodeGeneration(page: Page) {
  // Wait for QR code to load
  const qrCode = page.locator('[data-testid="qr-code"]');
  await expect(qrCode).toBeVisible({ timeout: 10000 });
  
  // Verify QR code has src attribute
  const src = await qrCode.getAttribute('src');
  expect(src).toBeTruthy();
  expect(src).toMatch(/^data:image\/png;base64,/);
}

async function verifyAddressCopy(page: Page) {
  const copyButton = page.getByRole('button', { name: 'Copy' });
  await expect(copyButton).toBeVisible();
  
  // Mock clipboard API
  await page.evaluate(() => {
    Object.assign(navigator, {
      clipboard: {
        writeText: () => Promise.resolve()
      }
    });
  });
  
  await copyButton.click();
  await expect(page.getByText('Copied!')).toBeVisible();
  
  // Verify copy state resets
  await expect(page.getByText('Copy')).toBeVisible({ timeout: 5000 });
}

async function simulatePaymentConfirmation(page: Page) {
  // Mock payment status API
  await page.route('/api/crypto-payments/*/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'Confirmed',
        transactionHash: '0x1234567890abcdef',
        confirmations: 6,
        completedAt: new Date().toISOString()
      })
    });
  });
  
  // Trigger status check
  await page.getByRole('button', { name: 'Check Status' }).click();
}

async function verifyOrderCompletion(page: Page) {
  await expect(page.getByText('Payment Confirmed')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Continue to Order' })).toBeVisible();
  
  await page.getByRole('button', { name: 'Continue to Order' }).click();
  await expect(page.getByText(/order confirmed|thank you/i)).toBeVisible();
}

async function mockTransactionDetection(page: Page) {
  await page.route('/api/crypto-payments/*/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'Pending',
        transactionHash: '0x1234567890abcdef',
        confirmations: 0
      })
    });
  });
}

async function mockConfirmationProgress(page: Page) {
  let confirmations = 1;
  
  await page.route('/api/crypto-payments/*/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'Pending',
        transactionHash: '0x1234567890abcdef',
        confirmations: confirmations++
      })
    });
  });
}

async function mockFinalConfirmation(page: Page) {
  await page.route('/api/crypto-payments/*/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'Confirmed',
        transactionHash: '0x1234567890abcdef',
        confirmations: 6,
        completedAt: new Date().toISOString()
      })
    });
  });
}