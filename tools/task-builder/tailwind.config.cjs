module.exports = {
  content: [require('node:path').resolve(__dirname, '../../task_builder_tailwind.html')],
  theme: { extend: {
    fontFamily: {
      sans: ['"Noto Sans TC Variable"', '"Plus Jakarta Sans Variable"', 'sans-serif'],
      mono: ['"JetBrains Mono Variable"', 'monospace'],
    },
    colors: { brand: { 50: '#f0fdf4', 100: '#dcfce7', 500: '#22c55e', 600: '#16a34a', 700: '#15803d', 800: '#166534' } },
  } },
};
