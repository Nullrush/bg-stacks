(async () => {
  const upcomingList  = document.getElementById('upcomingList');
  const archiveList   = document.getElementById('archiveList');
  const upcomingEmpty = document.getElementById('upcomingEmpty');
  const archiveEmpty  = document.getElementById('archiveEmpty');

  let events = [];
  try {
    const res = await fetch('/api/events');
    if (res.ok) events = await res.json();
  } catch { /* render empty gracefully */ }

  const upcoming = events.filter(e => e.isUpcoming);
  const archive  = events.filter(e => !e.isUpcoming);

  function getSafeEventUrl(rawUrl) {
    if (typeof rawUrl !== 'string') return '/';
    try {
      const url = new URL(rawUrl, window.location.origin);
      return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : '/';
    } catch {
      return '/';
    }
  }

  function renderEvent(e) {
    const li = document.createElement('li');
    li.className = 'event-card';
    const link = document.createElement('a');
    link.className = 'event-link';
    link.href = getSafeEventUrl(e.url);

    const name = document.createElement('span');
    name.className = 'event-name';
    name.textContent = e.name ?? '';

    const date = document.createElement('span');
    date.className = 'event-date dim';
    date.textContent = e.eventDate ?? '';

    link.append(name, date);
    li.appendChild(link);
    return li;
  }

  if (upcoming.length > 0) {
    upcoming.forEach(e => upcomingList.appendChild(renderEvent(e)));
  } else {
    upcomingEmpty.hidden = false;
  }

  if (archive.length > 0) {
    archive.forEach(e => archiveList.appendChild(renderEvent(e)));
  } else {
    archiveEmpty.hidden = false;
  }
})();
