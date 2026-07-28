#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
tmp_root="$(mktemp -d -p /tmp ironfamily-wsl-tests-XXXXXX)"
trap 'rm -rf "$tmp_root"' EXIT

dotnet_path="$(command -v dotnet || true)"
if [[ -z "${dotnet_path}" ]]; then
  windows_dotnet='/mnt/c/Program Files/dotnet/dotnet.exe'
  if [[ -x "${windows_dotnet}" ]]; then
    mkdir -p "${HOME}/bin"
    ln -sf "${windows_dotnet}" "${HOME}/bin/dotnet"
    export PATH="${HOME}/bin:${PATH}"
  fi
fi

command -v dotnet >/dev/null

if command -v ninja >/dev/null; then
  generator="Ninja"
else
  generator="Unix Makefiles"
fi

native_build="${tmp_root}/native-build"
native_install="${tmp_root}/native-install"
consumer_src="${tmp_root}/consumer-src"
consumer_build="${tmp_root}/consumer-build"

mkdir -p "${consumer_src}/subdir"

cat > "${consumer_src}/CMakeLists.txt" <<'EOF'
cmake_minimum_required(VERSION 3.20)
project(ironfamily_consumer C CXX)

find_package(IronFamily CONFIG REQUIRED)

add_executable(consumer_icfg consumer_icfg.c)
target_link_libraries(consumer_icfg PRIVATE IronFamily::ICFG)

add_executable(consumer_ilog consumer_ilog.c)
target_link_libraries(consumer_ilog PRIVATE IronFamily::ILOG)

add_executable(consumer_iupd consumer_iupd.c)
target_link_libraries(consumer_iupd PRIVATE IronFamily::IUPD)

add_executable(consumer_all consumer_all.cpp)
target_link_libraries(consumer_all PRIVATE IronFamily::ICFG IronFamily::ILOG IronFamily::IUPD)

add_subdirectory(subdir)
EOF

cat > "${consumer_src}/consumer_icfg.c" <<'EOF'
#include <ironcfg/ironcfg.h>

int main(void)
{
    return 0;
}
EOF

cat > "${consumer_src}/consumer_ilog.c" <<'EOF'
#include <ironcfg/ilog.h>

int main(void)
{
    return 0;
}
EOF

cat > "${consumer_src}/consumer_iupd.c" <<'EOF'
#include <ironfamily/ota_apply.h>
#include <ironfamily/iupd_reader.h>

int main(void)
{
    return 0;
}
EOF

cat > "${consumer_src}/consumer_all.cpp" <<'EOF'
#include <ironcfg/ironcfg.h>
#include <ironcfg/ilog.h>
#include <ironfamily/ota_apply.h>

int main()
{
    return 0;
}
EOF

cat > "${consumer_src}/subdir/CMakeLists.txt" <<'EOF'
add_executable(consumer_subdir consumer_subdir.c)
target_link_libraries(consumer_subdir PRIVATE IronFamily::ICFG)
EOF

cat > "${consumer_src}/subdir/consumer_subdir.c" <<'EOF'
#include <ironcfg/ironcfg.h>

int main(void)
{
    return 0;
}
EOF

cmake -S "${repo_root}/native" -B "${native_build}" -G "${generator}"
cmake --build "${native_build}"
ctest --test-dir "${native_build}" --output-on-failure

cmake --install "${native_build}" --prefix "${native_install}"

cmake -S "${consumer_src}" -B "${consumer_build}" -DIronFamily_DIR="${native_install}/lib/cmake/IronFamily"
cmake --build "${consumer_build}"

"${consumer_build}/consumer_icfg"
"${consumer_build}/consumer_ilog"
"${consumer_build}/consumer_iupd"
"${consumer_build}/consumer_all"
"${consumer_build}/subdir/consumer_subdir"

dotnet restore "${repo_root}/libs/ironconfig-dotnet/IronConfig.sln"
dotnet build "${repo_root}/libs/ironconfig-dotnet/IronConfig.sln" -c Release --no-restore
dotnet test "${repo_root}/libs/ironconfig-dotnet/IronConfig.sln" -c Release --no-build

"${repo_root}/tools/docs_truth_gate/verify_docs_truth.sh" "${repo_root}"
git -C "${repo_root}" diff --check
