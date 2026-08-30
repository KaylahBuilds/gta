const toggle = document.querySelector('.nav-toggle');
const nav = document.querySelector('.site-nav');

if (toggle && nav) {
  toggle.addEventListener('click', () => {
    const open = nav.classList.toggle('open');
    toggle.setAttribute('aria-expanded', String(open));
  });
  nav.addEventListener('click', event => {
    if (event.target.closest('a')) {
      nav.classList.remove('open');
      toggle.setAttribute('aria-expanded', 'false');
    }
  });
}

const intelData = {
  trap: {
    code: 'NARCO_07',
    title: 'The block notices the weight.',
    copy: 'Your runner moves more product, the corner earns faster, and narco heat starts spilling into every business you operate nearby.',
    trap: 'Revenue ↑ · Narco heat ↑', swipe: 'Watcher risk +12%', blade: 'Client traffic disrupted'
  },
  swipe: {
    code: 'FRAUD_12',
    title: 'The pattern gets one line clearer.',
    copy: 'A late pull gives you richer data, but the file learns your district, your hour, and the type of machine you keep returning to.',
    trap: 'Front scrutiny +8%', swipe: 'Data +186 · File ↑', blade: 'Wash capacity reserved'
  },
  blade: {
    code: 'VICE_03',
    title: 'New ground never stays quiet.',
    copy: 'Taking a rival corner changes the roster pool, moves vice attention, and gives every crew in the city a reason to test your reach.',
    trap: 'New protection route', swipe: 'Fresh-ground bonus', blade: 'Territory +1 · Rival anger ↑'
  }
};

document.querySelectorAll('.intel-tab').forEach(button => {
  button.addEventListener('click', () => {
    const data = intelData[button.dataset.intel];
    document.querySelectorAll('.intel-tab').forEach(tab => {
      const active = tab === button;
      tab.classList.toggle('active', active);
      tab.setAttribute('aria-selected', String(active));
    });
    document.querySelector('#intel-code').textContent = data.code;
    document.querySelector('#intel-title').textContent = data.title;
    document.querySelector('#intel-copy').textContent = data.copy;
    document.querySelector('#impact-trap').textContent = data.trap;
    document.querySelector('#impact-swipe').textContent = data.swipe;
    document.querySelector('#impact-blade').textContent = data.blade;
    document.querySelector('.intel-map')?.setAttribute('data-active', button.dataset.intel);
  });
});
