import type { ContextOptions, EffortLevel, Proposal, ThinkingLevel } from '../types/aiCommandCenter';

export const WAITING_VERBS = [
  'Accomplishing', 'Actioning', 'Actualizing', 'Baking', 'Beaming', 'Beboppin',
  'Befuddling', 'Billowing', 'Blanching', 'Bloviating', 'Boogieing', 'Boondoggling',
  'Bootstrapping', 'Booping', 'Brewing', 'Burrowing', 'Calculating', 'Caramelizing',
  'Cascading', 'Capturing', 'Cerebrating', 'Channelling', 'Choreographing', 'Churning',
  'Clauding', 'Coalescing', 'Cogitating', 'Composing', 'Combobulating', 'Concocting',
  'Considering', 'Contemplating', 'Cooking', 'Crafting', 'Creating', 'Crunching',
  'Crystallizing', 'Cultivating', 'Deciphering', 'Deliberating', 'Determining',
  'Discombobulating', 'Distilling', 'Doing', 'Dilly-dallying', 'Doodling', 'Ebbing',
  'Effecting', 'Elucidating', 'Embellishing', 'Enchanting', 'Envisioning', 'Evaporating',
  'Fermenting', 'Fiddle-fadding', 'Finagling', 'Flambéing', 'Flibbertigibbeting',
  'Flowing', 'Flummoxing', 'Forging', 'Forming', 'Frosting', 'Frolicking', 'Gallivanting',
  'Generating', 'Germinating', 'Gitifying', 'Grooving', 'Gusting', 'Hatching', 'Herding',
  'Hibernating', 'Honking', 'Hullaballooing', 'Hyperspacing', 'Ideating', 'Imagining',
  'Incubating', 'Inferring', 'Infusing', 'Jitterbugging', 'Julienning', 'Kneading',
  'Leavening', 'Levitating', 'Lollygagging', 'Manifesting', 'Marinating', 'Meandering',
  'Misting', 'Moseying', 'Mulling', 'Mustering', 'Musing', 'Nebulizing', 'Noodling',
  'Nucleating', 'Orbiting', 'Perambulating', 'Percolating', 'Perusing', 'Philosophising',
  'Photosynthesizing', 'Pontificating', 'Pondering', 'Pollinating', 'Precipitating',
  'Processing', 'Proofing', 'Propagating', 'Puttering', 'Puzzling', 'Quantumizing',
  'Razzle-dazzling', 'Recombobulating', 'Reticulating', 'Ruminating', 'Scheming',
  'Schlepping', 'Scurrying', 'Scampering', 'Seasoning', 'Shenaniganing', 'Shimming',
  'Shimmying', 'Simmering', 'Skedaddling', 'Sketching', 'Slithering', 'Smooshing',
  'Spelunking', 'Spinning', 'Sprouting', 'Stewing', 'Sublimating', 'Sussing', 'Swooping',
  'Symbioting', 'Synthesizing', 'Tempering', 'Thinking', 'Thundering', 'Tinkering',
  'Topsy-turvying', 'Transfiguring', 'Transmuting', 'Trick-or-treating', 'Twisting',
  'Unfurling', 'Unravelling', 'Vibing', 'Waddling', 'Wandering', 'Warping',
  'Whatchamacalliting', 'Whirlpooling', 'Whirring', 'Whisking', 'Wibbling', 'Working',
  'Wrangling', 'Zesting', 'Zigzagging'
];

export const thinkingLevels: ThinkingLevel[] = [
  { id: 'off', label: 'Off' },
  { id: 'adaptive', label: 'Adaptive' }
];

export const effortLevels: EffortLevel[] = [
  { id: 'off', label: 'Off' },
  { id: 'low', label: 'Low' },
  { id: 'medium', label: 'Medium' },
  { id: 'high', label: 'High' },
  { id: 'max', label: 'Max' }
];

export const contextOptions: ContextOptions = {
  regulations: [
    { id: 'wheelchair', label: 'Wheelchair Access (ADA)' },
    { id: 'feng-shui', label: 'Feng Shui Principles' },
    { id: 'fire-code', label: 'Fire Safety Code' }
  ],
  attachments: [
    { id: 'upload', label: 'Upload Image...' },
    { id: 'docs', label: 'Project Requirements.pdf' }
  ]
};

export const proposalMocks: Proposal[] = [
  {
    id: 'A',
    name: 'Ultimate Storage',
    tags: ['Storage++', 'Flow-'],
    metrics: { storage: '12.5m³', flow: 'Compact' },
    insight: 'Sacrificed 10% open space for max storage.',
    color: '#4facfe',
    thumbnailPattern: 'radial-gradient(circle at 30% 30%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)'
  },
  {
    id: 'B',
    name: 'Flow Priority',
    tags: ['Flow++', 'Open'],
    metrics: { storage: '8.0m³', flow: 'Excellent' },
    insight: 'Optimized for 1200mm main walkways.',
    color: '#00f2fe',
    thumbnailPattern: 'radial-gradient(circle at 70% 70%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)'
  },
  {
    id: 'C',
    name: 'Minimalist',
    tags: ['Light++', 'Cost-'],
    metrics: { storage: '6.5m³', flow: 'Good' },
    insight: 'Removed non-essential partitions.',
    color: '#a18cd1',
    thumbnailPattern: 'radial-gradient(circle at 50% 50%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #fbc2eb 0%, #a6c1ee 100%)'
  }
];
