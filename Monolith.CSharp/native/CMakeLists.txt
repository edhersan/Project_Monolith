cmake_minimum_required(VERSION 3.14)
project(tts_native VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Optional: find libopus
find_package(PkgConfig QUIET)
if(PkgConfig_FOUND)
    pkg_check_modules(OPUS libopus)
endif()

add_library(tts_native SHARED
    tts_native.cpp
    tts_native.h
)

target_include_directories(tts_native PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

if(OPUS_FOUND)
    target_compile_definitions(tts_native PRIVATE HAS_LIBOPUS)
    target_include_directories(tts_native PRIVATE ${OPUS_INCLUDE_DIRS})
    target_link_libraries(tts_native PRIVATE ${OPUS_LIBRARIES})
    message(STATUS "libopus found, Opus encoding enabled")
else()
    message(STATUS "libopus not found, using PCM-only mode (sine wave)")
endif()

# Set output directory for .NET runtime loading
set(OUTPUT_DIR "${CMAKE_SOURCE_DIR}/Monolith.Console/bin/Debug/net8.0/runtimes")
if(WIN32)
    set(PLATFORM "win-x64")
elseif(APPLE)
    set(PLATFORM "osx-x64")
else()
    set(PLATFORM "linux-x64")
endif()

set_target_properties(tts_native PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY "${OUTPUT_DIR}/${PLATFORM}/native"
    RUNTIME_OUTPUT_DIRECTORY "${OUTPUT_DIR}/${PLATFORM}/native"
    POSITION_INDEPENDENT_CODE ON
)
