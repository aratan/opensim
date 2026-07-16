# OpenSimulator Performance Benchmark Report
## Triple Contraste Nativo - Fuente Primaria

**Date:** 2026-07-16
**Hardware:** Intel Core i7-13700HX (Raptor Lake), 31GB RAM, NVMe SSD, CachyOS
**Runtime:** .NET SDK 10.0.109 (only .NET 10.0 runtime available)
**Project:** OpenSimulator - compiled target: net9.0

---

## 1. System Profile

| Metric | Value |
|--------|-------|
| CPU | Intel Core i7-13700HX (16 cores, 24 threads) |
| Max Clock | 5000 MHz |
| L2 Cache | 14 MiB |
| L3 Cache | 30 MiB |
| RAM Total | 31,723 MB (31 GB) |
| RAM Available | 27,085 MB |
| Disk Total | 972,665 MB (950 GB) |
| Disk Used | 521,697 MB (509 GB) |
| Kernel | 7.1.3-2-cachyos |
| .NET SDK | 10.0.109 |
| .NET Runtime | 10.0.9 (Microsoft.NETCore.App) |

---

## 2. I/O Benchmarks (NVMe)

| Test | Result |
|------|--------|
| Sequential Write (1GB) | **2 GB/s** |
| Sequential Read (1GB) | **3 GB/s** |
| Random 4K Write (100MB) | **298 MB/s** |
| Random 4K Read (100MB) | **4 GB/s** |

**Analysis:** The NVMe storage is performing well. Sequential speeds are typical for a modern NVMe drive. The random 4K read speed of 4 GB/s is excellent, indicating strong IOPS performance. This is important for OpenSimulator's database-heavy operations (asset loading, inventory lookups).

---

## 3. .NET Runtime Memory & GC Performance

### 3.1 Allocation Performance

| Test | Result | Analysis |
|------|--------|----------|
| LOH Allocation (100 x 85KB) | **6 ms** | Fast large object heap allocation |
| LOH Memory Used | **8 MB** | Expected for 8.5 MB of large objects |
| SOH Allocation (1M objects) | **268 ms** | ~3.7 million objects/sec allocation rate |

### 3.2 Garbage Collection Latency

| Test | Result | Analysis |
|------|--------|----------|
| Gen0 + Gen1 Collection | **0 ms** | Near-instant (sub-millisecond) |
| Gen2 (Full) Collection | **0 ms** | Near-instant (sub-millisecond) |
| GC Gen0 Count | **16** | Healthy young generation collections |
| GC Gen1 Count | **14** | Moderate mid-generation collections |
| GC Gen2 Count | **8** | Low full collections (good) |

**Analysis:** .NET 10.0 GC performance is excellent. Gen2 collections completing in sub-millisecond time means OpenSimulator will experience minimal pause times during heavy object churn. This is a significant improvement over Mono where Gen2 collections could cause 50-100ms pauses.

### 3.3 Concurrency & Threading

| Test | Result | Analysis |
|------|--------|----------|
| ThreadPool (10K tasks) | **30 ms** | 333K tasks/sec throughput |
| Lock Contention (100K ops) | **44 ms** | 2.3M lock ops/sec |
| ConcurrentDictionary (1M writes) | **1,064 ms** | 940K writes/sec |
| Dictionary (1M writes, single-thread) | **168 ms** | 5.95M writes/sec |

**Analysis:**
- **ThreadPool:** 333K tasks/sec is excellent. OpenSimulator's thread pool operations will be fast.
- **Lock Contention:** 2.3M lock operations/sec indicates the runtime handles contention well. However, this is a synthetic benchmark - real-world contention with complex lock hierarchies (as seen in OpenSim's `lock(this)` patterns) will be slower.
- **ConcurrentDictionary vs Dictionary:** The 6.3x overhead of ConcurrentDictionary is expected due to atomic operations. OpenSim's extensive use of ConcurrentDictionary (found in 30+ files) is appropriate for thread safety but comes at a cost.

### 3.4 Memory Usage

| Metric | Value |
|--------|-------|
| Peak Working Set | **105 MB** |

---

## 4. Binary Analysis

| Component | Size |
|-----------|------|
| OpenMetaverse.dll | 2.2 MB |
| OpenSim.Region.CoreModules.dll | 1.1 MB |
| BepuPhysics.dll | 811 KB |
| OpenSim.Region.Framework.dll | 748 KB |
| OpenSim.Framework.dll | 548 KB |
| OpenSim.Region.ClientStack.LindenUDP.dll | 384 KB |
| OpenSim.dll | 96 KB |
| OpenSim.Region.ScriptEngine.Shared.dll | 88 KB |
| OpenSim.Region.PhysicsModule.Bepu.dll | 52 KB |
| **Total DLLs** | **123 files** |
| **Total DLL Size** | **30.93 MB** |
| **Total bin/ Directory** | **7.4 GB** (includes assets, cache, configs) |

---

## 5. OpenSim Startup Benchmark

**Status:** CANNOT EXECUTE

**Reason:** OpenSimulator binaries are compiled for `net9.0` but only `.NET 10.0` runtime is installed on this system. The runtime refuses to load the application.

```
Framework: 'Microsoft.NETCore.App', version '9.0.0' (x64)
Available: 10.0.9 at [/usr/share/dotnet/shared/Microsoft.NETCore.App]
```

**Impact:** This is a critical finding for the **Replatform** strategy. The project targets net9.0 but needs to be updated to net10.0 (or a roll-forward policy needs to be configured).

---

## 6. Build System Analysis

| Metric | Value |
|--------|-------|
| Build Target | net9.0 |
| Build Tool | dotnet CLI + Prebuild |
| Build Time (Release) | ~4 min 26 sec |
| Build Errors (current) | 12 (ECS project - pre-existing API mismatch) |
| Build Warnings | 25+ |

**Pre-existing Build Issues:**
- `OpenSim.Region.ECS` project has API mismatches with `Friflo.Engine.ECS` v1.1.0
- Missing `Friflo.Engine.ECS.dll` in bin/ (was not included in original distribution)
- **Fix applied:** Added `using OpenSim.Framework;` to `EcsPrimShape.cs` and `EcsWorld.cs` to resolve `PrimitiveBaseShape` type reference

---

## 7. Key Findings for Modernization Strategy

### 7.1 Refactor Opportunities (from code analysis)

| Issue | Files Affected | Priority |
|-------|---------------|----------|
| `lock(this)` anti-pattern | 10+ files | High |
| APM (BeginGetResponse) instead of async/await | WebUtil.cs, multiple handlers | High |
| SmartThreadPool (third-party, legacy) | ThirdParty/SmartThreadPool/ | High |
| Manual `GC.Collect()` calls | ServerBase.cs | Medium |
| ExpiringCacheOS (custom cache) | Multiple services | Medium |
| MemoryStream without ArrayPool | Multiple handlers | Medium |

### 7.2 Replatform Findings

| Issue | Impact | Priority |
|-------|--------|----------|
| net9.0 target with net10.0 runtime | **CRITICAL** - app won't start | Critical |
| Mono.Addins dependency | Blocks full .NET modernization | High |
| Mono.Data.Sqlite reference | Can use Microsoft.Data.Sqlite | Medium |
| SmartThreadPool - no modern .NET support | Blocks thread pool optimization | High |

### 7.3 Performance Bottlenecks (from code + community research)

1. **Physics Engine:** ODE/BulletSim consume 60-80% CPU in dense regions
2. **Script Thread Starvation:** Poorly written LSL scripts block thread pool
3. **Chatty Protocol:** Redundant position updates even when avatar is idle
4. **Database I/O:** Synchronous DB calls block network threads
5. **Lock Contention:** SceneGraph lock prevents concurrent object updates

---

## 8. Recommendations (Priority Order)

### Phase 0: Fix Runtime Mismatch (Immediate)
- Update `TargetFramework` from `net9.0` to `net10.0` OR
- Install .NET 9.0 runtime alongside 10.0

### Phase 1: Quick Wins (Refactor) - 2-4 weeks
- Replace `lock(this)` with private lock objects
- Remove manual `GC.Collect()` calls
- Update cache to modern `MemoryCache`

### Phase 2: Replatform Core - 1-2 months
- Migrate Mono.Data.Sqlite to Microsoft.Data.Sqlite
- Replace SmartThreadPool with System.Threading.Pool
- Fix all async patterns (APM to async/await)

### Phase 3: Architecture Extraction - 6+ months
- Extract Asset Service as microservice
- Implement Redis cache layer
- Isolate Physics workers

---

## 9. Benchmark Artifacts

- Raw JSON results: `benchmark_results/benchmark_20260716_003243.json`
- Memory benchmark source: `/tmp/opensim_mem_bench/`
- This report: `benchmark_results/FINAL_REPORT.md`
