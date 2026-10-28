// Service worker cho trang test Web Push (focus-test.html). Nhận tin đẩy từ server khi tab đã đóng và hiện thành thông báo hệ điều hành.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

// Server gửi payload JSON: { title, body, action }. action là loại thông báo (START_PROMPT, NEAR_END, OVERRUN, ...)
self.addEventListener('push', (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { title: 'METANOIA', body: event.data ? event.data.text() : '' };
  }

  event.waitUntil(self.registration.showNotification(data.title || 'METANOIA', {
    body: data.body || '',
    tag: data.action || 'metanoia',
    data
  }));
});

// Bấm vào thông báo: mở hoặc đưa tab focus-test lên trước
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
      for (const client of windows) {
        if ('focus' in client) return client.focus();
      }
      return self.clients.openWindow('/focus-test.html');
    })
  );
});
