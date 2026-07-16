#!/bin/bash
# =============================================================================
# OpenSimulator Performance Benchmark (using existing binaries)
# Hardware: Intel Core i7-13700HX, 30GB RAM, NVMe SSD, RTX 4060
# =============================================================================

set -e

OPENSIM_DIR="/home/aratan/Proyectos/opensim"
BENCHMARK_DIR="$OPENSIM_DIR/benchmark_results"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS_FILE="$BENCHMARK_DIR/benchmark_${TIMESTAMP}.json"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

mkdir -p "$BENCHMARK_DIR"

echo -e "${BLUE}╔══════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║    OpenSimulator Performance Benchmark - Phase 1        ║${NC}"
echo -e "${BLUE}║    Triple Contraste Nativo: Fuente Primaria            ║${NC}"
echo -e "${BLUE}╚══════════════════════════════════════════════════════════╝${NC}"
echo ""

# Capture results in structured format
echo "{" > "$RESULTS_FILE"
echo "  \"timestamp\": \"$(date -Iseconds)\"," >> "$RESULTS_FILE"
echo "  \"hardware\": {" >> "$RESULTS_FILE"

# =============================================================================
# Section 1: System Information
# =============================================================================
echo -e "${CYAN}[1/5] System Information${NC}"

CPU_MODEL=$(lscpu | grep 'Model name' | cut -d: -f2 | xargs)
CPU_CORES=$(nproc)
CPU_THREADS=$(lscpu | grep 'Thread' | sed 's/.*: *//' | xargs)
RAM_TOTAL=$(free -m | awk '/^Mem:/{print $2}')
RAM_AVAILABLE=$(free -m | awk '/^Mem:/{print $7}')
DISK_TOTAL=$(df -m / | awk 'NR==2{print $2}')
DISK_USED=$(df -m / | awk 'NR==2{print $3}')
KERNEL=$(uname -r)
DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "N/A")

echo "    \"cpu\": \"$CPU_MODEL\"," >> "$RESULTS_FILE"
echo "    \"cores\": $CPU_CORES," >> "$RESULTS_FILE"
echo "    \"threads\": $CPU_THREADS," >> "$RESULTS_FILE"
echo "    \"ram_total_mb\": $RAM_TOTAL," >> "$RESULTS_FILE"
echo "    \"ram_available_mb\": $RAM_AVAILABLE," >> "$RESULTS_FILE"
echo "    \"disk_total_mb\": $DISK_TOTAL," >> "$RESULTS_FILE"
echo "    \"disk_used_mb\": $DISK_USED," >> "$RESULTS_FILE"
echo "    \"kernel\": \"$KERNEL\"," >> "$RESULTS_FILE"
echo "    \"dotnet_sdk\": \"$DOTNET_VERSION\"" >> "$RESULTS_FILE"
echo "  }," >> "$RESULTS_FILE"

echo -e "  ${GREEN}✓ CPU: $CPU_MODEL${NC}"
echo -e "  ${GREEN}✓ Cores: $CPU_CORES ($CPU_THREADS threads) | RAM: ${RAM_TOTAL}MB${NC}"
echo ""

# =============================================================================
# Section 2: NVMe I/O Benchmark
# =============================================================================
echo -e "${CYAN}[2/5] NVMe I/O Benchmark${NC}"
echo "  \"io_benchmarks\": {" >> "$RESULTS_FILE"

# Sequential Write (1GB)
echo -e "  ${YELLOW}Testing sequential write (1GB)...${NC}"
SEQ_WRITE=$(dd if=/dev/zero of=/tmp/os_bench_seq bs=1M count=1024 2>&1 | grep -oP '[\d.]+ [GMTK]B/s' | tail -1)
echo "    \"seq_write\": \"$SEQ_WRITE\"," >> "$RESULTS_FILE"

# Sequential Read (1GB)
echo -e "  ${YELLOW}Testing sequential read (1GB)...${NC}"
SEQ_READ=$(dd if=/tmp/os_bench_seq of=/dev/null bs=1M 2>&1 | grep -oP '[\d.]+ [GMTK]B/s' | tail -1)
echo "    \"seq_read\": \"$SEQ_READ\"," >> "$RESULTS_FILE"

# Random Write (4K blocks, 100MB)
echo -e "  ${YELLOW}Testing random 4K write (100MB)...${NC}"
RAND_WRITE=$(dd if=/dev/urandom of=/tmp/os_bench_rand bs=4k count=25600 2>&1 | grep -oP '[\d.]+ [GMTK]B/s' | tail -1)
echo "    \"rand_4k_write\": \"$RAND_WRITE\"," >> "$RESULTS_FILE"

# Random Read (4K blocks)
echo -e "  ${YELLOW}Testing random 4K read (100MB)...${NC}"
RAND_READ=$(dd if=/tmp/os_bench_rand of=/dev/null bs=4k 2>&1 | grep -oP '[\d.]+ [GMTK]B/s' | tail -1)
echo "    \"rand_4k_read\": \"$RAND_READ\"" >> "$RESULTS_FILE"

rm -f /tmp/os_bench_seq /tmp/os_bench_rand

echo "  }," >> "$RESULTS_FILE"
echo -e "  ${GREEN}✓ Seq Write: $SEQ_WRITE | Seq Read: $SEQ_READ${NC}"
echo -e "  ${GREEN}✓ Rand 4K Write: $RAND_WRITE | Rand 4K Read: $RAND_READ${NC}"
echo ""

# =============================================================================
# Section 3: Memory & GC Performance (.NET)
# =============================================================================
echo -e "${CYAN}[3/5] .NET Memory & GC Benchmark${NC}"
echo "  \"memory_gc\": {" >> "$RESULTS_FILE"

MEMBENCH_DIR="/tmp/opensim_mem_bench"
rm -rf "$MEMBENCH_DIR"
mkdir -p "$MEMBENCH_DIR"

cat > "$MEMBENCH_DIR/MemBench.csproj" << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Optimize>true</Optimize>
  </PropertyGroup>
</Project>
PROJ

cat > "$MEMBENCH_DIR/Program.cs" << 'CS'
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;

class Program
{
    static void Main()
    {
        var proc = Process.GetCurrentProcess();
        
        // Force baseline GC
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        long baselineMem = GC.GetTotalMemory(true);

        // ---- Test 1: Large Object Heap Allocation ----
        var sw = Stopwatch.StartNew();
        var largeArrays = new List<byte[]>();
        for (int i = 0; i < 100; i++)
        {
            largeArrays.Add(new byte[85000]); // Just above LOH threshold (85KB)
        }
        sw.Stop();
        long afterAlloc = GC.GetTotalMemory(false);
        Console.WriteLine($"LOH_ALLOC_MS={sw.ElapsedMilliseconds}");
        Console.WriteLine($"LOH_ALLOC_MB={(afterAlloc - baselineMem) / 1024 / 1024}");

        // ---- Test 2: Gen0/Gen1 Collection Latency ----
        largeArrays = null;
        GC.Collect(0, GCCollectionMode.Forced, false);
        sw.Restart();
        GC.Collect(1, GCCollectionMode.Forced, false);
        sw.Stop();
        Console.WriteLine($"GEN01_GC_MS={sw.ElapsedMilliseconds}");

        // ---- Test 3: Full GC (Gen2) Latency ----
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        sw.Restart();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        sw.Stop();
        Console.WriteLine($"GEN2_GC_MS={sw.ElapsedMilliseconds}");

        // ---- Test 4: Small Object Heap Allocation Speed ----
        sw.Restart();
        var smallObjects = new List<object>();
        for (int i = 0; i < 1000000; i++)
        {
            smallObjects.Add(new object());
        }
        sw.Stop();
        Console.WriteLine($"SOH_ALLOC_MS={sw.ElapsedMilliseconds}");

        // ---- Test 5: Thread Pool Performance ----
        int completed = 0;
        int totalTasks = 10000;
        var mre = new ManualResetEventSlim(false);
        
        sw.Restart();
        for (int i = 0; i < totalTasks; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (Interlocked.Increment(ref completed) == totalTasks)
                    mre.Set();
            });
        }
        mre.Wait();
        sw.Stop();
        Console.WriteLine($"THREADPOOL_MS={sw.ElapsedMilliseconds}");
        Console.WriteLine($"THREADPOOL_TASKS={totalTasks}");

        // ---- Test 6: Lock Contention ----
        object lockObj = new object();
        int counter = 0;
        int lockTasks = 100000;
        var lockMre = new ManualResetEventSlim(false);
        completed = 0;
        
        sw.Restart();
        for (int i = 0; i < lockTasks; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (lockObj) { counter++; }
                if (Interlocked.Increment(ref completed) == lockTasks)
                    lockMre.Set();
            });
        }
        lockMre.Wait();
        sw.Stop();
        Console.WriteLine($"LOCK_CONTENTION_MS={sw.ElapsedMilliseconds}");
        Console.WriteLine($"LOCK_COUNTER={counter}");

        // ---- Test 7: ConcurrentDictionary vs Dictionary ----
        sw.Restart();
        var cdict = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        Parallel.For(0, 1000000, i => cdict[i] = i);
        sw.Stop();
        Console.WriteLine($"CDICT_WRITE_MS={sw.ElapsedMilliseconds}");

        sw.Restart();
        var dict = new Dictionary<int, int>();
        for (int i = 0; i < 1000000; i++) dict[i] = i;
        sw.Stop();
        Console.WriteLine($"DICT_WRITE_MS={sw.ElapsedMilliseconds}");

        // ---- Final Memory Stats ----
        Console.WriteLine($"PEAK_MEM_MB={proc.WorkingSet64 / 1024 / 1024}");
        Console.WriteLine($"GC_TOTAL_MEM_MB={GC.GetTotalMemory(false) / 1024 / 1024}");
        Console.WriteLine($"GC_GEN0={GC.CollectionCount(0)}");
        Console.WriteLine($"GC_GEN1={GC.CollectionCount(1)}");
        Console.WriteLine($"GC_GEN2={GC.CollectionCount(2)}");
    }
}
CS

echo -e "  ${YELLOW}Running .NET memory & GC benchmark...${NC}"
cd "$MEMBENCH_DIR"
BENCH_OUTPUT=$(dotnet run -c Release 2>&1)
echo "$BENCH_OUTPUT" > /tmp/membench_output.txt

# Parse results
LOH_ALLOC_MS=$(echo "$BENCH_OUTPUT" | grep "LOH_ALLOC_MS=" | cut -d= -f2)
GEN01_GC_MS=$(echo "$BENCH_OUTPUT" | grep "GEN01_GC_MS=" | cut -d= -f2)
GEN2_GC_MS=$(echo "$BENCH_OUTPUT" | grep "GEN2_GC_MS=" | cut -d= -f2)
SOH_ALLOC_MS=$(echo "$BENCH_OUTPUT" | grep "SOH_ALLOC_MS=" | cut -d= -f2)
THREADPOOL_MS=$(echo "$BENCH_OUTPUT" | grep "THREADPOOL_MS=" | cut -d= -f2)
LOCK_CONTENTION_MS=$(echo "$BENCH_OUTPUT" | grep "LOCK_CONTENTION_MS=" | cut -d= -f2)
CDICT_WRITE_MS=$(echo "$BENCH_OUTPUT" | grep "CDICT_WRITE_MS=" | cut -d= -f2)
DICT_WRITE_MS=$(echo "$BENCH_OUTPUT" | grep "DICT_WRITE_MS=" | cut -d= -f2)
PEAK_MEM_MB=$(echo "$BENCH_OUTPUT" | grep "PEAK_MEM_MB=" | cut -d= -f2)

echo "    \"loh_alloc_ms\": $LOH_ALLOC_MS," >> "$RESULTS_FILE"
echo "    \"gen01_gc_ms\": $GEN01_GC_MS," >> "$RESULTS_FILE"
echo "    \"gen2_gc_ms\": $GEN2_GC_MS," >> "$RESULTS_FILE"
echo "    \"soh_alloc_1m_ms\": $SOH_ALLOC_MS," >> "$RESULTS_FILE"
echo "    \"threadpool_10k_ms\": $THREADPOOL_MS," >> "$RESULTS_FILE"
echo "    \"lock_contention_100k_ms\": $LOCK_CONTENTION_MS," >> "$RESULTS_FILE"
echo "    \"concurrent_dict_write_ms\": $CDICT_WRITE_MS," >> "$RESULTS_FILE"
echo "    \"dict_write_ms\": $DICT_WRITE_MS," >> "$RESULTS_FILE"
echo "    \"peak_memory_mb\": $PEAK_MEM_MB" >> "$RESULTS_FILE"
echo "  }," >> "$RESULTS_FILE"

rm -rf "$MEMBENCH_DIR"

echo -e "  ${GREEN}✓ LOH Alloc: ${LOH_ALLOC_MS}ms | Gen2 GC: ${GEN2_GC_MS}ms${NC}"
echo -e "  ${GREEN}✓ ThreadPool(10K): ${THREADPOOL_MS}ms | Lock(100K): ${LOCK_CONTENTION_MS}ms${NC}"
echo -e "  ${GREEN}✓ ConcurrentDict: ${CDICT_WRITE_MS}ms | Dict: ${DICT_WRITE_MS}ms${NC}"
echo -e "  ${GREEN}✓ Peak Memory: ${PEAK_MEM_MB}MB${NC}"
echo ""

# =============================================================================
# Section 4: OpenSim Binary Analysis
# =============================================================================
echo -e "${CYAN}[4/5] OpenSim Binary Analysis${NC}"
echo "  \"binaries\": {" >> "$RESULTS_FILE"

if [ -d "$OPENSIM_DIR/bin" ]; then
    MAIN_DLL=$(du -b "$OPENSIM_DIR/bin/OpenSim.dll" 2>/dev/null | cut -f1 || echo 0)
    FRAMEWORK_DLL=$(du -b "$OPENSIM_DIR/bin/OpenSim.Framework.dll" 2>/dev/null | cut -f1 || echo 0)
    REGION_DLL=$(du -b "$OPENSIM_DIR/bin/OpenSim.Region.Framework.dll" 2>/dev/null | cut -f1 || echo 0)
    BEPHYSICS_DLL=$(du -b "$OPENSIM_DIR/bin/OpenSim.Region.PhysicsModule.Bepu.dll" 2>/dev/null | cut -f1 || echo 0)
    TOTAL_SIZE=$(du -sb "$OPENSIM_DIR/bin" 2>/dev/null | cut -f1 || echo 0)
    DLL_COUNT=$(find "$OPENSIM_DIR/bin" -maxdepth 1 -name "*.dll" | wc -l)
    
    echo "    \"opensim_dll_bytes\": $MAIN_DLL," >> "$RESULTS_FILE"
    echo "    \"framework_dll_bytes\": $FRAMEWORK_DLL," >> "$RESULTS_FILE"
    echo "    \"region_framework_dll_bytes\": $REGION_DLL," >> "$RESULTS_FILE"
    echo "    \"bepu_physics_dll_bytes\": $BEPHYSICS_DLL," >> "$RESULTS_FILE"
    echo "    \"total_bin_size_bytes\": $TOTAL_SIZE," >> "$RESULTS_FILE"
    echo "    \"dll_count\": $DLL_COUNT" >> "$RESULTS_FILE"
    
    echo -e "  ${GREEN}✓ OpenSim.dll: $(echo "scale=2; $MAIN_DLL/1048576" | bc)MB | Total: $(echo "scale=2; $TOTAL_SIZE/1048576" | bc)MB${NC}"
    echo -e "  ${GREEN}✓ DLL count: $DLL_COUNT${NC}"
else
    echo "    \"error\": \"bin directory not found\"" >> "$RESULTS_FILE"
fi

echo "  }," >> "$RESULTS_FILE"
echo ""

# =============================================================================
# Section 5: OpenSim Startup Benchmark
# =============================================================================
echo -e "${CYAN}[5/5] OpenSim Startup Benchmark${NC}"
echo "  \"startup\": {" >> "$RESULTS_FILE"

if [ -f "$OPENSIM_DIR/bin/OpenSim.dll" ]; then
    cd "$OPENSIM_DIR/bin"
    
    # Measure startup time (with 15s timeout)
    echo -e "  ${YELLOW}Measuring OpenSim startup time (15s window)...${NC}"
    STARTUP_LOG="/tmp/opensim_startup_${TIMESTAMP}.log"
    
    STARTUP_START=$(date +%s%N)
    timeout 15 dotnet OpenSim.dll 2>&1 > "$STARTUP_LOG" || true
    STARTUP_END=$(date +%s%N)
    STARTUP_TIME=$(( (STARTUP_END - STARTUP_START) / 1000000 ))
    
    echo "    \"startup_time_ms\": $STARTUP_TIME," >> "$RESULTS_FILE"
    
    # Check for startup errors
    if grep -qi "error\|exception\|fail" "$STARTUP_LOG" 2>/dev/null; then
        ERRORS=$(grep -ci "error\|exception\|fail" "$STARTUP_LOG")
        echo "    \"startup_errors\": $ERRORS," >> "$RESULTS_FILE"
    else
        echo "    \"startup_errors\": 0," >> "$RESULTS_FILE"
    fi
    
    echo "    \"startup_log_lines\": $(wc -l < "$STARTUP_LOG")" >> "$RESULTS_FILE"
    
    echo -e "  ${GREEN}✓ Startup time: ${STARTUP_TIME}ms${NC}"
else
    echo "    \"error\": \"OpenSim.dll not found\"" >> "$RESULTS_FILE"
fi

echo "  }" >> "$RESULTS_FILE"

# =============================================================================
# Close JSON
# =============================================================================
echo "}" >> "$RESULTS_FILE"

echo ""
echo -e "${BLUE}╔══════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║              Benchmark Complete!                        ║${NC}"
echo -e "${BLUE}╚══════════════════════════════════════════════════════════╝${NC}"
echo -e "${GREEN}Results saved to: ${YELLOW}$RESULTS_FILE${NC}"
echo ""

# Print JSON nicely
echo -e "${CYAN}=== Full Results (JSON) ===${NC}"
cat "$RESULTS_FILE" | python3 -m json.tool 2>/dev/null || cat "$RESULTS_FILE"
