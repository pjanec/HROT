# Changelog

All notable changes to FastBTree will be documented in this file.

## [1.0.0] - 2026-01-05

### Added
- Core interpreter with zero-allocation execution
- Composites: Sequence, Selector, Parallel
- Decorators: Inverter, Repeater, Wait, Cooldown, ForceSuccess, ForceFailure
- Leaves: Action, Condition
- JSON authoring format
- Binary serialization with hot reload support
- Tree validation with warning detection for known limitations
- TreeVisualizer debug utility
- Comprehensive documentation (README, Quick Start, Design Docs)
- Example trees and console demo
- 72+ unit and integration tests

### Performance
- Zero allocations in interpreter hot path
- 8-byte nodes (cache-aligned)
- 64-byte execution state (single cache line)
- ~100,000 ticks/sec for typical trees

### Documentation
- Professional README with quick start
- Comprehensive Quick Start guide
- 6 design documents covering architecture
- 2 example JSON trees
- Working console demonstration application

## [0.1.0] - 2026-01-01 (Internal)

### Added
- Initial project structure
- Basic data structures
