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

  function renderEvent(e) {
    const li = document.createElement('li');
    li.className = 'event-card';
    li.innerHTML = `<a href="${e.url}" class="event-link">
      <span class="event-name">${e.name}</span>
      <span class="event-date dim">${e.eventDate}</span>
    </a>`;
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
