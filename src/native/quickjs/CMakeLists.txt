cmake_minimum_required(VERSION 3.20)
project(loot_qjs C)
if(MSVC)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
include(FetchContent)

FetchContent_Declare(quickjs_ng
  URL https://codeload.github.com/quickjs-ng/quickjs/tar.gz/d7215b0
  URL_HASH SHA256=7ce3155176baf5e47abc863073cdeab38f52676a0ebd4141cdf66ad9c4ca54e5)
FetchContent_GetProperties(quickjs_ng)
if(NOT quickjs_ng_POPULATED)
  FetchContent_Populate(quickjs_ng)
endif()
set(QUICKJS_SOURCE_DIR "${quickjs_ng_SOURCE_DIR}")
include(${CMAKE_CURRENT_LIST_DIR}/patch-quickjs.cmake)
configure_file(${quickjs_ng_SOURCE_DIR}/LICENSE ${CMAKE_CURRENT_BINARY_DIR}/QuickJS-NG-LICENSE.txt COPYONLY)

add_library(quickjs_ng_static STATIC
  ${quickjs_ng_SOURCE_DIR}/dtoa.c
  ${quickjs_ng_SOURCE_DIR}/libregexp.c
  ${quickjs_ng_SOURCE_DIR}/libunicode.c
  ${quickjs_ng_SOURCE_DIR}/quickjs.c)
set_target_properties(quickjs_ng_static PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_include_directories(quickjs_ng_static PUBLIC ${quickjs_ng_SOURCE_DIR})
target_compile_definitions(quickjs_ng_static PRIVATE _GNU_SOURCE)
target_compile_features(quickjs_ng_static PRIVATE c_std_11)
find_package(Threads REQUIRED)
target_link_libraries(quickjs_ng_static PUBLIC ${CMAKE_DL_LIBS} Threads::Threads)
if(UNIX)
  target_link_libraries(quickjs_ng_static PUBLIC m)
endif()

add_library(loot_qjs SHARED loot_qjs.c)
target_link_libraries(loot_qjs PRIVATE quickjs_ng_static)
target_include_directories(loot_qjs PRIVATE ${quickjs_ng_SOURCE_DIR})
target_compile_features(loot_qjs PRIVATE c_std_11)
set_target_properties(loot_qjs PROPERTIES C_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN ON OUTPUT_NAME "loot_qjs")
if(WIN32)
  set_target_properties(loot_qjs PROPERTIES PREFIX "")
endif()
if(APPLE)
  set_target_properties(loot_qjs PROPERTIES MACOSX_RPATH TRUE INSTALL_NAME_DIR "@rpath")
endif()
