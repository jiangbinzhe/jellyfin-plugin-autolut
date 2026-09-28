'use strict';
// Derived from user-supplied v6.7.5; see core-source.json.
    const CONFIG = {
        lutSize: 33,
        analysisLongSide: 640,
        autoReanalyze: false,
        reanalyzeInterval: 300000,
        initialDelayMs: 500,
        defaultEnabled: true,
        maxSkinPixels: 3000,
        sampleFrames: 1,
        sampleIntervalMs: 120,
        mediapipeVersion: '0.10.14',
        mediapipeModelUrl: 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task',
        faceBoxExpand: 1.0,
        pollIntervalMs: 500,
        debugFaceBox: false,
        preloadMediaPipe: true,
        coarseSampleSize: 400,
        enableStatsCache: true,
        lumGuardTolerance: 0.005,
        balanceParams: true,
        paramStrength: {
            temp: 0.6,
            highlight: 0.3,
            shadow: 0.6,
            contrast: 0.4,
        },
        balanceMultiplier: 1.0,
        analyzeRetryCooldownMs: 3000,
        canvasMatchVideo: 1,
        canvasResizeThreshold: 4,
        severeSatThreshold: 0.45,
        severeSatMin: -15,
        autoHlCompBrightThreshold: 15,
        autoHlCompStrength: 0.5,
        autoHlCompMinOrigHl: 0.01,
        nearWhiteDelta: 0.08,
        nearWhiteMinC: 0.85,
        brightGuardMaxAdd: 2,
        highlightGuardMaxAdd: 1,
        singleFrameTimeoutMs: 500,
        minSkinPixels: 150,
        faceBoxMinSkinPixels: 150,
        fullScanTargetPixels: 40000,
        // 肤色/偏色判定
        skinRBHighThreshold: 999,
        highRBInherentMax: 1.15,
        highRBColorCastMin: 1.25,
        highPixelMinCount: 20,
        highLumThreshold: 0.7,
        highFallbackLumThreshold: 0.8,
        highFallbackSatMax: 0.15,
        inherentYellowRatio: 0.85,
        // 缺亮补亮
        highPixelLowThreshold: 200,
        highPixelDarkSkinMax: 0.52,
        highPixelForceBright: 20,
        // 额外降色温（综合误差）
        extraTempSatMin: 0.43,
        extraTempRBMin: 1.7,
        extraTempShadowMin: 0.018,
        extraTempHlMax: 0.04,
        extraTempStep: 2,
        extraTempMaxSteps: 25,
        extraTempSatWeight: 2,
        // 全图高光统计
        globalHlSampleStep: 2,
        // ★ v6.7 采样区域肤色检测
        skinDetection: {
            crCoverage: 0.90,
            fallbackCrRange: [130, 180],
            staticCbRange: [77, 127],
        },
        // ★ v6.7.2 高光过量场景阈值
        highHlScene: {
            hlRatioMin: 0.45,
            lumMin: 0.62,
            highRBMin: 1.25,
            hlCompMin: -18,
            highlightHardMin: -18,
            hlHiCap: 0.06,
            tempExtraMin: -10,
            contrastTargetShift: 0.05,
            contrastDevWeight: 0.0001,
            satTargetFactor: 1.05,
            satMaxExtra: 3,
            shadowMaxFactor: 0.5,
            brightMaxExtra: 5,
        },
    };

    const KR = 0.2126, KG = 0.7152, KB = 0.0722;
    const HL_THRESHOLD = 0.70;
    const SH_THRESHOLD = 0.20;
    const OVEREXPOSE_LIMIT = 0.96;
    const HL_BRIGHT_DAMP_START = 0.7;
    const HL_BRIGHT_DAMP_STRENGTH = 0.7;
    const HL_BRIGHT_DAMP_MIN = 0.3;
    const HL_CONTRAST_DAMP_STRENGTH = 0.5;
    const HL_CONTRAST_DAMP_MIN = 0.5;
    const FACE_SKIN_LUM_MAX = 0.9;

    const JY_TEMPLATE = {
        temp: -20, tint: 0, sat: 0, bright: 0,
        contrast: 0, highlight: -2, shadow: 2,
    };
    const JY_ADJUST_RANGE = {
        temp: [-30, 4], tint: [-6, 6], sat: [-6, 4],
        bright: [-8, 50], contrast: [-4, 3],
        highlight: [-6, 4], shadow: [-6, 6],
    };
    const TARGETS = {
        skin_rb: [1.20, 1.50],
        skin_rg: [1.15, 1.35],
        skin_lum: [0.40, 0.58],
        skin_sat: [0.12, 0.26],
        full_contrast: [0.10, 0.20],
        full_hl_ratio: [0.00, 0.20],
        full_sh_ratio: [0.00, 0.40],
    };

    const JY_TEMPLATE_HIGH_MATCH = {
        temp: -5, tint: 0, sat: 1, bright: 4,
        contrast: 1, highlight: -3, shadow: 3,
    };
    const JY_ADJUST_RANGE_HIGH_MATCH = {
        temp: [-24, -4], tint: [-6, 6], sat: [-6, 4],
        bright: [-6, 6], contrast: [-4, 3],
        highlight: [-3, 3], shadow: [-6, 6],
    };
    const JY_ADJUST_RANGE_HIGH_MATCH_CENTER = {
        temp: [-30, -4],
        tint: [-7, 7],
        sat: [-7, 4],
        bright: [-12, 9],
        contrast: [-4, 3],
        highlight: [-7, 3],
        shadow: [-7, 7],
    };
    const TARGETS_HIGH_MATCH = {
        skin_rb:       [1.40, 1.55],
        skin_rg:       [1.24, 1.36],
        skin_lum:      [0.38, 0.62],
        skin_sat:      [0.12, 0.42],
        full_contrast: [0.12, 0.24],
        full_hl_ratio: [0.00, 0.15],
        full_sh_ratio: [0.00, 0.24],
    };

    const JY_TEMPLATE_EXTREME_DARK = {
        temp: -18, tint: 3, sat: -1, bright: 25,
        contrast: -2, highlight: -1, shadow: 20,
    };
    const JY_ADJUST_RANGE_EXTREME_DARK = {
        temp: [-8, 4], tint: [-3, 5], sat: [-5, 1],
        bright: [-15, 20], contrast: [-3, 2],
        highlight: [-3, 2], shadow: [-15, 15],
    };
    const TARGETS_EXTREME_DARK = {
        skin_rb: [1.05, 1.32],
        skin_rg: [1.12, 1.28],
        skin_lum: [0.46, 0.66],
        skin_sat: [0.14, 0.25],
        full_contrast: [0.13, 0.23],
        full_hl_ratio: [0.00, 0.15],
        full_sh_ratio: [0.14, 0.35],
    };

    // ============================================================
    // 工具函数
    // ============================================================
    function rangeError(value, targetRange) {
        const lo = targetRange[0], hi = targetRange[1];
        if (value < lo) return lo - value;
        if (value > hi) return value - hi;
        return 0.0;
    }

    function getDynamicHlTarget(skinLum) {
        const lumLo = 0.35, lumHi = 0.54;
        const hlHiAtLumLo = 0.02;
        const hlHiAtLumHi = 0.10;
        const ratio = Math.max(0, Math.min(1,
            (skinLum - lumLo) / Math.max(lumHi - lumLo, 1e-6)));
        const hlHi = hlHiAtLumLo + (hlHiAtLumHi - hlHiAtLumLo) * ratio;
        return [0.00, hlHi];
    }

    function median(arr) {
        if (!arr || arr.length === 0) return 0;
        const sorted = [...arr].sort((a, b) => a - b);
        const mid = Math.floor(sorted.length / 2);
        return sorted.length % 2
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    function isExtremeDark(info) {
        if (!info) return false;
        if (info.no_skin_data) return false;
        if (info.skin_rb > 1.6) return false;
        return (
            info.skin_lum < 0.22 &&
            info.full_sh_ratio > 0.35 &&
            info.full_hl_ratio < 0.15
        );
    }

    function isHighMatch(info) {
        if (!info) return false;
        if (info.no_skin_data) return false;
        if (isExtremeDark(info)) return false;
        // ★ 高光像素数门槛
        const hp = info.high_pixels !== undefined ? info.high_pixels : 0;
        if (hp <= 500) return false;
        return (
            info.skin_rb >= TARGETS_HIGH_MATCH.skin_rb[0] &&
            info.skin_rb <= TARGETS_HIGH_MATCH.skin_rb[1] &&
            info.skin_rg >= TARGETS_HIGH_MATCH.skin_rg[0] &&
            info.skin_rg <= TARGETS_HIGH_MATCH.skin_rg[1] &&
            info.skin_lum >= TARGETS_HIGH_MATCH.skin_lum[0] &&
            info.skin_lum <= TARGETS_HIGH_MATCH.skin_lum[1] &&
            info.skin_sat >= TARGETS_HIGH_MATCH.skin_sat[0] &&
            info.skin_sat <= TARGETS_HIGH_MATCH.skin_sat[1]
        );
    }

    function subsample(pixels, maxN) {
        const n = pixels.length / 3;
        if (n <= maxN) return pixels;
        const step = n / maxN;
        const out = new Float32Array(maxN * 3);
        for (let i = 0; i < maxN; i++) {
            const s = Math.floor(i * step) * 3;
            out[i * 3] = pixels[s];
            out[i * 3 + 1] = pixels[s + 1];
            out[i * 3 + 2] = pixels[s + 2];
        }
        return out;
    }

    function isNearWhite(r, g, b) {
        const maxC = Math.max(r, g, b);
        const minC = Math.min(r, g, b);
        return (maxC - minC) < CONFIG.nearWhiteDelta && minC > CONFIG.nearWhiteMinC;
    }

    // ============================================================
    // 全局分析计数器
    // ============================================================
    let _analyzingCount = 0;

    // ============================================================
    // 参数力度协调器
    // ============================================================
    function balanceParamStrength(paramsIn, template, adjustRange, isHighHlScene) {
        if (!CONFIG.balanceParams) return paramsIn;

        const out = Object.assign({}, paramsIn);

        const S = CONFIG.paramStrength;
        const M = CONFIG.balanceMultiplier;

        const tempDelta = out.temp - template.temp;
        const tempDarken = tempDelta < 0 ? -tempDelta : 0;
        const tempEquiv = tempDarken * S.temp;

        const hlDelta = out.highlight - template.highlight;
        const hlDarken = hlDelta < 0 ? -hlDelta : 0;
        const hlEquiv = isHighHlScene ? 0 : hlDarken * S.highlight;

        const shDelta = out.shadow - template.shadow;
        const shDarken = shDelta < 0 ? -shDelta : 0;
        const shEquiv = shDarken * S.shadow;

        const cDelta = out.contrast - template.contrast;
        const cDarken = cDelta < 0 ? -cDelta : 0;
        const cEquiv = cDarken * S.contrast;

        const totalDarken = (tempEquiv + hlEquiv + shEquiv + cEquiv) * M;

        const brightCompensation = out.bright - template.bright;

        const deficit = totalDarken - brightCompensation;

        if (deficit > 0.5) {
            const addBright = Math.round(deficit);
            const bMax = template.bright + adjustRange.bright[1];
            const newBright = Math.min(out.bright + addBright, bMax);
            const actualAdded = newBright - out.bright;
            out.bright = newBright;

            if (actualAdded !== 0) {
                console.log(
                    `[LUT-力度协调] 压暗总量=${totalDarken.toFixed(2)}档 ` +
                    `(色温${tempEquiv.toFixed(2)}+高光${hlEquiv.toFixed(2)}+` +
                    `阴影${shEquiv.toFixed(2)}+对比度${cEquiv.toFixed(2)})` +
                    (M !== 1.0 ? ` ×${M}` : '') + `, ` +
                    `bright补偿=${brightCompensation.toFixed(2)}档, ` +
                    `缺口=${deficit.toFixed(2)}档, ` +
                    `bright ${brightCompensation >= 0 ? '+' : ''}${brightCompensation.toFixed(0)}` +
                    ` → +${out.bright - template.bright} ` +
                    `(实际补${actualAdded >= 0 ? '+' : ''}${actualAdded})`
                );
            }

            const stillDeficit = totalDarken - (out.bright - template.bright);
            if (stillDeficit > 0.5) {
                const hlMax = template.highlight + adjustRange.highlight[1];
                const hlDeficit = Math.round(stillDeficit);
                const newHl = Math.min(out.highlight + hlDeficit, hlMax);
                const actualHlAdded = newHl - out.highlight;

                if (actualHlAdded > 0) {
                    out.highlight = newHl;
                    console.log(
                        `[LUT-力度协调] bright 到顶，抬 highlight ` +
                        `${actualHlAdded >= 0 ? '+' : ''}${actualHlAdded} ` +
                        `→ ${newHl}`
                    );
                }
            }
        }

        return out;
    }

    // ============================================================
    // 一、调色算法
    // ============================================================
    function applyTempTintScalar(r, g, b, temp, tint) {
        const t = temp / 50.0;
        const ti = tint / 50.0;
        let rGain = 1.0 + t * 0.23;
        let gGain = 1.0 + t * 0.058;
        let bGain = 1.0 - t * 0.23;
        rGain *= 1.0 + ti * 0.045;
        gGain *= 1.0 - ti * 0.09;
        bGain *= 1.0 + ti * 0.045;
        const lumBefore = KR + KG + KB;
        const lumAfter = KR * rGain + KG * gGain + KB * bGain;
        if (lumAfter > 0.001) {
            const scale = lumBefore / lumAfter;
            rGain *= scale; gGain *= scale; bGain *= scale;
        }
        const lum = KR * r + KG * g + KB * b;
        const hlStart = 0.70;
        const hlWeight = lum > hlStart
            ? Math.min(1.0, (lum - hlStart) / (1.0 - hlStart)) : 0.0;
        const protectStrength = 0.95;
        const hlDamp = 1.0 - hlWeight * protectStrength;
        const rGainEff = 1.0 + (rGain - 1.0) * hlDamp;
        const gGainEff = 1.0 + (gGain - 1.0) * hlDamp;
        const bGainEff = 1.0 + (bGain - 1.0) * hlDamp;
        return [r * rGainEff, g * gGainEff, b * bGainEff];
    }

    function applySaturationScalar(r, g, b, sat) {
        if (sat === 0) return [r, g, b];
        const gray = KR * r + KG * g + KB * b;
        // ★ 系数从 0.82 提升到 1.5，让 sat 效果更明显
        const satScale = 1.0 + sat / 50.0 * 1.5;
        return [
            gray + satScale * (r - gray),
            gray + satScale * (g - gray),
            gray + satScale * (b - gray),
        ];
    }

    function applyBrightnessScalar(r, g, b, bright) {
        if (bright === 0) return [r, g, b];
        const lum = KR * r + KG * g + KB * b;
        const t = lum > HL_BRIGHT_DAMP_START
            ? (lum - HL_BRIGHT_DAMP_START) / (1.0 - HL_BRIGHT_DAMP_START) : 0.0;
        let hlDamp = 1.0 - t * HL_BRIGHT_DAMP_STRENGTH;
        hlDamp = Math.max(HL_BRIGHT_DAMP_MIN, hlDamp);
        const brightScale = 1.0 + bright / 50.0 * 0.67 * hlDamp;
        return [r * brightScale, g * brightScale, b * brightScale];
    }

    function applyContrastScalar(r, g, b, contrast) {
        if (contrast === 0) return [r, g, b];
        const lum = KR * r + KG * g + KB * b;
        const t = lum > HL_BRIGHT_DAMP_START
            ? (lum - HL_BRIGHT_DAMP_START) / (1.0 - HL_BRIGHT_DAMP_START) : 0.0;
        let hlDamp = 1.0 - t * HL_CONTRAST_DAMP_STRENGTH;
        hlDamp = Math.max(HL_CONTRAST_DAMP_MIN, hlDamp);
        const contrastScale = 1.0 + contrast / 50.0 * 0.57 * hlDamp;
        return [
            0.5 + contrastScale * (r - 0.5),
            0.5 + contrastScale * (g - 0.5),
            0.5 + contrastScale * (b - 0.5),
        ];
    }

    function applyHighlightShadowScalar(r, g, b, highlight, shadow) {
        if (highlight === 0 && shadow === 0) return [r, g, b];
        const lum = KR * r + KG * g + KB * b;
        if (highlight !== 0) {
            const hlW = lum > 0.5 ? (lum - 0.5) / 0.5 : 0.0;
            const hlScale = 1.0 + highlight / 50.0 * 0.7 * hlW;
            r *= hlScale; g *= hlScale; b *= hlScale;
        }
        if (shadow !== 0) {
            const shW = lum < 0.5 ? (0.5 - lum) / 0.5 : 0.0;
            const shScale = 1.0 + shadow / 50.0 * 0.7 * shW;
            r *= shScale; g *= shScale; b *= shScale;
        }
        return [r, g, b];
    }

    function applyParamsToSkinArray(pixels, params) {
        const n = pixels.length / 3;
        const out = new Float32Array(n * 3);
        for (let i = 0; i < n; i++) {
            let r = pixels[i * 3];
            let g = pixels[i * 3 + 1];
            let b = pixels[i * 3 + 2];

            let t = applyBrightnessScalar(r, g, b, params.bright);
            r = t[0]; g = t[1]; b = t[2];
            t = applyHighlightShadowScalar(r, g, b, params.highlight, params.shadow);
            r = t[0]; g = t[1]; b = t[2];
            t = applyTempTintScalar(r, g, b, params.temp, params.tint);
            r = t[0]; g = t[1]; b = t[2];
            t = applySaturationScalar(r, g, b, params.sat);
            r = t[0]; g = t[1]; b = t[2];
            t = applyContrastScalar(r, g, b, params.contrast);
            r = t[0]; g = t[1]; b = t[2];

            const lum = KR * r + KG * g + KB * b;
            if (lum > OVEREXPOSE_LIMIT && !isNearWhite(r, g, b)) {
                const s = OVEREXPOSE_LIMIT / lum;
                r *= s; g *= s; b *= s;
            }
            out[i * 3] = Math.min(1, Math.max(0, r));
            out[i * 3 + 1] = Math.min(1, Math.max(0, g));
            out[i * 3 + 2] = Math.min(1, Math.max(0, b));
        }
        return out;
    }

    function sampleSkinStats(pixels) {
        const n = pixels.length / 3;
        if (n === 0) return null;
        let sR = 0, sG = 0, sB = 0, sSat = 0;
        let hl = 0, sh = 0;
        const lums = new Float32Array(n);
        for (let i = 0; i < n; i++) {
            const r = pixels[i * 3], g = pixels[i * 3 + 1], b = pixels[i * 3 + 2];
            sR += r; sG += g; sB += b;
            const lum = KR * r + KG * g + KB * b;
            lums[i] = lum;
            const max = Math.max(r, g, b), min = Math.min(r, g, b);
            sSat += max > 1e-6 ? (max - min) / max : 0;
            if (lum > HL_THRESHOLD) hl++;
            if (lum < SH_THRESHOLD) sh++;
        }
        const mR = sR / n, mG = sG / n, mB = sB / n;
        const mLum = KR * mR + KG * mG + KB * mB;
        let varSum = 0;
        for (let i = 0; i < n; i++) {
            const d = lums[i] - mLum;
            varSum += d * d;
        }
        return {
            skin_rb: mB > 1e-6 ? mR / mB : 1.0,
            skin_rg: mG > 1e-6 ? mR / mG : 1.0,
            skin_lum: mLum,
            skin_sat: sSat / n,
            full_contrast: Math.sqrt(varSum / n),
            full_hl_ratio: hl / n,
            full_sh_ratio: sh / n,
        };
    }

    // ============================================================
    // 二、参数搜索
    // ============================================================
    function makeStatsCache() {
        let key = '', val = null;
        return function (pixels, params) {
            const k = params.bright + '|' + params.temp + '|' + params.tint + '|' +
                params.sat + '|' + params.contrast + '|' +
                params.highlight + '|' + params.shadow + '|' + pixels.length;
            if (k === key && val) return val;
            val = sampleSkinStats(applyParamsToSkinArray(pixels, params));
            key = k;
            return val;
        };
    }

    function predictParams(info, skinPixels, useExtreme, useHighMatch) {
        const template = useExtreme ? JY_TEMPLATE_EXTREME_DARK :
            useHighMatch ? JY_TEMPLATE_HIGH_MATCH : JY_TEMPLATE;

        // ★ 中央 50% 判断（必须在 adjustRange 之前）
        const isCenterFallback = (info.regionName === '中央50%');

        let adjustRange;
        if (useExtreme) {
            adjustRange = JY_ADJUST_RANGE_EXTREME_DARK;
        } else if (useHighMatch && isCenterFallback) {
            adjustRange = JY_ADJUST_RANGE_HIGH_MATCH_CENTER;
        } else if (useHighMatch) {
            adjustRange = JY_ADJUST_RANGE_HIGH_MATCH;
        } else {
            adjustRange = JY_ADJUST_RANGE;
        }
        const targets = useExtreme ? TARGETS_EXTREME_DARK :
            useHighMatch ? TARGETS_HIGH_MATCH : TARGETS;
        const hlWeight = useExtreme ? 10 : (useHighMatch ? 18 : 15);

        if (useHighMatch && isCenterFallback) {
            console.log(
                `[LUT-调整范围] 中央50% + 高命中模板，使用更大的调整范围：` +
                `temp=[${JY_ADJUST_RANGE_HIGH_MATCH_CENTER.temp[0]}, ${JY_ADJUST_RANGE_HIGH_MATCH_CENTER.temp[1]}]，` +
                `bright=[${JY_ADJUST_RANGE_HIGH_MATCH_CENTER.bright[0]}, ${JY_ADJUST_RANGE_HIGH_MATCH_CENTER.bright[1]}]`
            );
        }

        // ★ 中央 50% 目标：色温/色调/饱和/对比/高光下移；亮度/阴影上移
        const TARGETS_CENTER = {
            skin_rb:       [1.05, 1.35],
            skin_rg:       [1.15, 1.35],
            skin_lum:      [0.55, 0.75],
            skin_sat:      [0.08, 0.22],
            full_contrast: [0.10, 0.20],
            full_hl_ratio: [0.00, 0.20],
            full_sh_ratio: [0.00, 0.40],
        };
        // ★ 中央 50% + 高命中模板：色温/色调/饱和/对比下移；亮度上移；饱和度/对比度也上移
        const TARGETS_CENTER_HIGH_MATCH = {
            skin_rb:       [0.90, 1.20],
            skin_rg:       [1.30, 1.50],
            skin_lum:      [0.45, 0.65],
            skin_sat:      [0.10, 0.20],
            full_contrast: [0.18, 0.28],
            full_hl_ratio: [0.00, 0.20],
            full_sh_ratio: [0.00, 0.40],
        };
        let targetsEff;
        if (isCenterFallback && useHighMatch) {
            targetsEff = TARGETS_CENTER_HIGH_MATCH;
        } else if (isCenterFallback) {
            targetsEff = TARGETS_CENTER;
        } else {
            targetsEff = targets;
        }

        // ★ 中央 50% 时 hlWeight 从 50 降到 10
        const hlWeightEff = isCenterFallback ? 10 : hlWeight;

        if (isCenterFallback && useHighMatch) {
            console.log(
                `[LUT-目标] 中央50%采样（高命中模板），使用 TARGETS_CENTER_HIGH_MATCH：` +
                `skin_lum=[${TARGETS_CENTER_HIGH_MATCH.skin_lum[0]}, ${TARGETS_CENTER_HIGH_MATCH.skin_lum[1]}]，` +
                `skin_rb=[${TARGETS_CENTER_HIGH_MATCH.skin_rb[0]}, ${TARGETS_CENTER_HIGH_MATCH.skin_rb[1]}]，` +
                `skin_sat=[${TARGETS_CENTER_HIGH_MATCH.skin_sat[0]}, ${TARGETS_CENTER_HIGH_MATCH.skin_sat[1]}]`
            );
        } else if (isCenterFallback) {
            console.log(
                `[LUT-目标] 中央50%采样（标准模板），目标区间下移：` +
                `skin_lum=[${TARGETS_CENTER.skin_lum[0]}, ${TARGETS_CENTER.skin_lum[1]}]，` +
                `skin_rb=[${TARGETS_CENTER.skin_rb[0]}, ${TARGETS_CENTER.skin_rb[1]}]，` +
                `skin_sat=[${TARGETS_CENTER.skin_sat[0]}, ${TARGETS_CENTER.skin_sat[1]}]`
            );
        }

        const origLum = info.skin_lum;
        const origSat = info.skin_sat;
        const skinRB = info.skin_rb;
        const highRB = info.high_rb !== undefined ? info.high_rb : 1.0;
        const highPixels = info.high_pixels !== undefined ? info.high_pixels : 0;
        const fullContrast = info.full_contrast !== undefined ? info.full_contrast : 0.15;

        // ★ 高光过量场景
        const HH = CONFIG.highHlScene;
        const isHighHlScene = !useExtreme && (
            (info.full_hl_ratio || 0) > HH.hlRatioMin &&
            origLum > HH.lumMin &&
            highRB > HH.highRBMin
        );
        if (isHighHlScene) {
            console.log(
                `[LUT-高光过量场景] full_hl_ratio=${(info.full_hl_ratio||0).toFixed(3)} ` +
                `> ${HH.hlRatioMin} 且 skin_lum=${origLum.toFixed(3)} > ${HH.lumMin} ` +
                `且 high_rb=${highRB.toFixed(3)} > ${HH.highRBMin} ` +
                `→ 启用激进高光/色温/对比度策略`
            );
        }

        let skinInherentlyYellow = false;
        let sceneColorCast = false;
        if (skinRB > CONFIG.skinRBHighThreshold &&
            highPixels >= CONFIG.highPixelMinCount) {
            if (highRB < CONFIG.highRBInherentMax) {
                skinInherentlyYellow = true;
                console.log(
                    `[LUT-肤色判定] 肤色 R/B=${skinRB.toFixed(3)} 高，` +
                    `但高光 R/B=${highRB.toFixed(3)} 正常（${highPixels} 像素）→ ` +
                    `判定为肤色本身偏黄，色温不猛降`
                );
            } else if (highRB > CONFIG.highRBColorCastMin) {
                sceneColorCast = true;
                console.log(
                    `[LUT-肤色判定] 肤色 R/B=${skinRB.toFixed(3)} 高，` +
                    `高光 R/B=${highRB.toFixed(3)} 也偏黄（${highPixels} 像素）→ ` +
                    `判定为画面偏色，色温正常纠正`
                );
            }
        }

        const lowHighPixels = (highPixels < CONFIG.highPixelLowThreshold);
        const darkSkin = (origLum < CONFIG.highPixelDarkSkinMax);
        const needForceBright = lowHighPixels && darkSkin && !useExtreme;

        const origShRatio = info.full_sh_ratio !== undefined ? info.full_sh_ratio : 0;
        const origHlRatio = info.full_hl_ratio !== undefined ? info.full_hl_ratio : 0;

        const satAndRBOk = (origSat > CONFIG.extraTempSatMin &&
                            skinRB > CONFIG.extraTempRBMin);
        const shadowOk = (origShRatio > CONFIG.extraTempShadowMin);
        const highlightOk = (origHlRatio <= CONFIG.extraTempHlMax);

        const needExtraTemp = satAndRBOk && shadowOk && highlightOk;

        if (needExtraTemp) {
            console.log(
                `[LUT-额外降色温判定] skin_sat=${origSat.toFixed(3)} ` +
                `> ${CONFIG.extraTempSatMin} 且 skin_rb=${skinRB.toFixed(3)} ` +
                `> ${CONFIG.extraTempRBMin} 且 full_sh_ratio=${origShRatio.toFixed(4)} ` +
                `> ${CONFIG.extraTempShadowMin} 且 full_hl_ratio=${origHlRatio.toFixed(4)} ` +
                `<= ${CONFIG.extraTempHlMax} → 色温搜索后将额外降色温`
            );
        }

        const coarse = subsample(skinPixels, CONFIG.coarseSampleSize);
        const statsCached = CONFIG.enableStatsCache ? makeStatsCache() : null;
        const statsOfCoarse = statsCached
            ? (p) => statsCached(coarse, p)
            : (p) => sampleSkinStats(applyParamsToSkinArray(coarse, p));

        let params = Object.assign({}, template);

        // ---- 色温搜索 ----
        if (useHighMatch) {
            const rb = info.skin_rb;
            const [rbLo, rbHi] = targets.skin_rb;
            let tempBase;
            if (rb <= rbLo) tempBase = -3;
            else if (rb >= rbHi) tempBase = -8;
            else {
                const ratio = (rb - rbLo) / Math.max(rbHi - rbLo, 1e-6);
                tempBase = Math.round(-3 + ratio * (-8 - (-3)));
            }
            let bestTemp = tempBase, bestErr = Infinity;
            for (let d = -2; d <= 2; d++) {
                const p = Object.assign({}, params, { temp: tempBase + d });
                const detected = statsOfCoarse(p);
                let err = Math.pow(rangeError(detected.skin_rb, targets.skin_rb), 2);
                err += 0.001 * (d * d);
                if (err < bestErr) { bestErr = err; bestTemp = tempBase + d; }
            }
            params.temp = bestTemp;
        } else {
            const infoRb = info.skin_rb;
            const baseDetected = statsOfCoarse(params);
            const baseRb = baseDetected.skin_rb;
            let rbOffset = infoRb - baseRb;
            rbOffset = Math.max(-0.08, Math.min(0.08, rbOffset));

            const [rbLo, rbHi] = targetsEff.skin_rb;
            let rbTarget80 = rbLo + (rbHi - rbLo) * 0.8;

            if (skinInherentlyYellow) {
                const origTarget = rbTarget80;
                rbTarget80 = Math.max(rbTarget80, skinRB * CONFIG.inherentYellowRatio);
                console.log(
                    `[LUT-色温] 肤色本身偏黄，R/B 目标从 ` +
                    `${origTarget.toFixed(3)} 调整为 ${rbTarget80.toFixed(3)}`
                );
            }

            if (isHighHlScene) {
                const origTarget = rbTarget80;
                rbTarget80 = rbLo + (rbHi - rbLo) * 0.45;
                console.log(
                    `[LUT-色温] 高光过量场景，R/B 目标从 ${origTarget.toFixed(3)} ` +
                    `下调到 ${rbTarget80.toFixed(3)}`
                );
            }

            // ★ 中央 50% 时 skin_rg、skin_lum 权重从 5、10 降到 1、1
            const rgW = isCenterFallback ? 1 : 5;
            const lumW = isCenterFallback ? 1 : 10;

            let bestTemp = params.temp, bestErr = Infinity;
            const tMin = isHighHlScene
                ? template.temp + adjustRange.temp[0] + HH.tempExtraMin
                : template.temp + adjustRange.temp[0];
            const tMax = template.temp + adjustRange.temp[1];
            for (let temp = tMin; temp <= tMax; temp += 1) {
                const p = Object.assign({}, params, { temp });
                const detected = statsOfCoarse(p);
                const correctedRb = detected.skin_rb + rbOffset;
                const rbErr = Math.max(0, Math.abs(correctedRb - rbTarget80) - 0.03);
                let err = 15 * rbErr * rbErr;
                err += rgW * Math.pow(rangeError(detected.skin_rg, targetsEff.skin_rg), 2);
                err += lumW * Math.pow(rangeError(detected.skin_lum, targetsEff.skin_lum), 2);
                err += 0.0005 * (temp - template.temp) * (temp - template.temp);
                if (err < bestErr) { bestErr = err; bestTemp = temp; }
            }
            params.temp = bestTemp;
        }

        // ---- 额外降色温 ----
        if (needExtraTemp && !useExtreme) {
            const [rbLo, rbHi] = targetsEff.skin_rb;
            const rbTarget80 = rbLo + (rbHi - rbLo) * 0.8;
            const satTarget = (targetsEff.skin_sat[0] + targetsEff.skin_sat[1]) / 2;

            const tMin = isHighHlScene
                ? template.temp + adjustRange.temp[0] + HH.tempExtraMin
                : template.temp + adjustRange.temp[0];

            let temp = params.temp;
            let extra = 0;
            const maxExtra = CONFIG.extraTempMaxSteps;
            const step = CONFIG.extraTempStep;
            const satW = CONFIG.extraTempSatWeight;

            const prevDetected = statsOfCoarse(params);
            const prevRBErr = Math.abs(prevDetected.skin_rb - rbTarget80);
            const prevSatErr = Math.max(0, prevDetected.skin_sat - satTarget);
            let prevScore = prevRBErr + prevSatErr * satW;

            while (extra < maxExtra) {
                const nextTemp = temp - step;
                if (nextTemp < tMin) break;

                const p = Object.assign({}, params, { temp: nextTemp });
                const detected = statsOfCoarse(p);
                const rbErr = Math.abs(detected.skin_rb - rbTarget80);
                const satErr = Math.max(0, detected.skin_sat - satTarget);
                const score = rbErr + satErr * satW;

                if (score >= prevScore) break;

                temp = nextTemp;
                params.temp = temp;
                prevScore = score;
                extra++;
            }

            if (extra > 0) {
                console.log(
                    `[LUT-额外降色温] sat=${origSat.toFixed(3)} > ${CONFIG.extraTempSatMin} ` +
                    `且 R/B=${skinRB.toFixed(3)} > ${CONFIG.extraTempRBMin} → ` +
                    `色温继续降低 ${extra} 步（每步 ${step}），temp = ${params.temp}`
                );
            }
        }

        // ---- 亮度搜索 ----
        {
            const [lumLo, lumHi] = targetsEff.skin_lum;
            let lumTarget = lumLo + (lumHi - lumLo) * 0.8;

            let bestBright = params.bright, bestErr = Infinity;
            const bMin = template.bright + adjustRange.bright[0];
            let bMax = template.bright + adjustRange.bright[1];

            // ★ 动态放开 bMax：不小于 |temp| × 1.2
            if (!useExtreme) {
                const tempBasedMax = Math.round(Math.abs(params.temp) * 1.2);
                const originalBMax = bMax;
                bMax = Math.max(bMax, tempBasedMax);
                if (bMax !== originalBMax) {
                    console.log(
                        `[LUT-亮度] |temp| = ${Math.abs(params.temp)} → ` +
                        `动态 bMax ${originalBMax} → ${bMax}`
                    );
                }
            }

            if (isHighHlScene) {
                bMax += HH.brightMaxExtra;
                console.log(
                    `[LUT-亮度] 高光过量场景，bright 上限放宽到 ${bMax}`
                );
            }

            for (let bright = bMin; bright <= bMax; bright += 1) {
                const p = Object.assign({}, params, { bright });
                const detected = statsOfCoarse(p);
                const curLum = detected.skin_lum;
                let lumErr;
                if (curLum < lumTarget) lumErr = Math.pow(lumTarget - curLum, 2);
                else if (curLum > lumHi) lumErr = Math.pow(curLum - lumTarget, 2);
                else lumErr = 0;
                let err = lumErr;
                err += hlWeightEff * Math.pow(rangeError(detected.full_hl_ratio, targetsEff.full_hl_ratio), 2);
                if (err < bestErr) { bestErr = err; bestBright = bright; }
            }
            params.bright = bestBright;
        }

        // ---- 亮度复检 ----
        {
            const [lumLo, lumHi] = targetsEff.skin_lum;
            let lumTarget = lumLo + (lumHi - lumLo) * 0.8;
            const postDetected = statsOfCoarse(params);
            const postLum = postDetected.skin_lum;
            if (postLum < lumTarget * 0.9 || postLum > lumTarget * 1.1) {
                let bestBright2 = params.bright, bestErr2 = Infinity;
                const baseBright = params.bright;
                const lo2 = Math.max(adjustRange.bright[0],
                    baseBright - 8 - template.bright);
                const hi2 = Math.min(adjustRange.bright[1],
                    baseBright + 8 - template.bright);
                for (let delta = lo2; delta <= hi2; delta += 1) {
                    const bright = template.bright + delta;
                    const p = Object.assign({}, params, { bright });
                    const detected = statsOfCoarse(p);
                    let lumErr;
                    if (detected.skin_lum < lumTarget) lumErr = Math.pow(lumTarget - detected.skin_lum, 2);
                    else if (detected.skin_lum > lumHi) lumErr = Math.pow(detected.skin_lum - lumTarget, 2);
                    else lumErr = 0;
                    let err = lumErr;
                    err += hlWeightEff * Math.pow(rangeError(detected.full_hl_ratio, targetsEff.full_hl_ratio), 2);
                    if (err < bestErr2) { bestErr2 = err; bestBright2 = bright; }
                }
                params.bright = bestBright2;
            }
        }

        // ---- 色调搜索 ----
        {
            const rgTarget = (targetsEff.skin_rg[0] + targetsEff.skin_rg[1]) / 2;
            let bestTint = params.tint, bestErr = Infinity;
            const tMin = template.tint + adjustRange.tint[0];
            const tMax = template.tint + adjustRange.tint[1];
            for (let tint = tMin; tint <= tMax; tint += 1) {
                const p = Object.assign({}, params, { tint });
                const detected = statsOfCoarse(p);
                let err = Math.pow(rangeError(detected.skin_rg,
                    [rgTarget * 0.95, rgTarget * 1.05]), 2);
                err += 0.001 * (tint - template.tint) * (tint - template.tint);
                if (err < bestErr) { bestErr = err; bestTint = tint; }
            }
            params.tint = bestTint;
            // ★ tint 至少 6，保证皮肤有血色
            if (params.tint < 6) {
                console.log(
                    `[LUT-色调] tint=${params.tint} < 6 → 强制为 6（加血色）`
                );
                params.tint = 6;
            }
        }

        // ---- 饱和度搜索 ----
        {
            let satTarget;
            if (isHighHlScene) {
                satTarget = targetsEff.skin_sat[1] * HH.satTargetFactor;
                console.log(
                    `[LUT-饱和度] 高光过量场景，饱和度目标抬到 ${satTarget.toFixed(3)}`
                );
            } else if (origSat !== undefined && origSat !== null && origSat > targetsEff.skin_sat[1]) {
                satTarget = targetsEff.skin_sat[1] * 0.95;
            } else {
                satTarget = (targetsEff.skin_sat[0] + targetsEff.skin_sat[1]) / 2;
            }

            let sMin, sMax;
            let satDeviationWeight = 0.001;
            // ★ 中央 50% 时禁用 severe 分支
            const severe = !isCenterFallback &&
                (origSat !== undefined && origSat !== null &&
                 origSat > CONFIG.severeSatThreshold);
            if (severe) {
                sMin = CONFIG.severeSatMin;
                sMax = 0;
                satDeviationWeight = 0.00005;
                console.log(
                    `[LUT-饱和度] 原片饱和度=${origSat.toFixed(3)} > ` +
                    `${CONFIG.severeSatThreshold}，扩大搜索范围到 [${sMin}, ${sMax}]，` +
                    `偏离惩罚权重=${satDeviationWeight}`
                );
            } else {
                sMin = template.sat + adjustRange.sat[0];
                sMax = template.sat + adjustRange.sat[1];
                if (isHighHlScene) sMax += HH.satMaxExtra;
            }

            let bestSat = params.sat, bestErr = Infinity;
            for (let sat = sMin; sat <= sMax; sat += 1) {
                const p = Object.assign({}, params, { sat });
                const detected = statsOfCoarse(p);
                let err = Math.pow(rangeError(detected.skin_sat,
                    [satTarget * 0.9, satTarget * 1.1]), 2);
                err += satDeviationWeight * (sat - template.sat) * (sat - template.sat);
                if (err < bestErr) { bestErr = err; bestSat = sat; }
            }
            params.sat = bestSat;
            console.log(
                `[LUT-饱和度] 搜索完成，sat=${bestSat}，` +
                `检测值=${statsOfCoarse(params).skin_sat.toFixed(3)}，` +
                `目标区间=[${(satTarget * 0.9).toFixed(3)}, ${(satTarget * 1.1).toFixed(3)}]`
            );
        }

        // ---- 对比度搜索 ----
        {
            let bestContrast = params.contrast, bestErr = Infinity;
            const cMin = template.contrast + adjustRange.contrast[0];
            let cMax = template.contrast + adjustRange.contrast[1];
            if (isHighHlScene) cMax += 2;

            const contrastTarget = isHighHlScene
                ? [(targetsEff.full_contrast[0] + targetsEff.full_contrast[1]) / 2 + HH.contrastTargetShift,
                   targetsEff.full_contrast[1] + HH.contrastTargetShift]
                : targetsEff.full_contrast;

            const contrastDevW = isHighHlScene ? HH.contrastDevWeight : 0.001;

            for (let contrast = cMin; contrast <= cMax; contrast += 1) {
                const p = Object.assign({}, params, { contrast });
                const detected = statsOfCoarse(p);
                let err = Math.pow(rangeError(detected.full_contrast, contrastTarget), 2);
                err += contrastDevW * (contrast - template.contrast) * (contrast - template.contrast);
                if (err < bestErr) { bestErr = err; bestContrast = contrast; }
            }
            params.contrast = bestContrast;
        }

        // ---- 高光补偿 ----
        let hlCompensation = 0;
        const hlCompMin = isHighHlScene ? HH.hlCompMin : -10;
        if (!useExtreme) {
            const origHl = info.full_hl_ratio || 0;
            const origLumForHl = info.skin_lum || 0.5;
            const [, hlHiT] = getDynamicHlTarget(origLumForHl);
            if (origHl > hlHiT) {
                const brightBoost = Math.max(0, params.bright - 5);
                const shadowBoost = Math.max(0, params.shadow - 2);
                hlCompensation = Math.round(-(brightBoost * 1.0 + shadowBoost * 0.5));
                hlCompensation = Math.max(hlCompensation, hlCompMin);
            }
        }

        if (!useExtreme && params.bright > CONFIG.autoHlCompBrightThreshold) {
            const origHlForComp = info.full_hl_ratio || 0;
            if (origHlForComp < CONFIG.autoHlCompMinOrigHl) {
                console.log(
                    `[LUT-自动高光压缩] 原片高光=${origHlForComp.toFixed(4)} ` +
                    `< ${CONFIG.autoHlCompMinOrigHl}，原片本身高光极少，` +
                    `跳过自动高光压缩`
                );
            } else {
                const extra = Math.round(
                    (params.bright - CONFIG.autoHlCompBrightThreshold)
                    * CONFIG.autoHlCompStrength
                );
                if (extra > 0) {
                    hlCompensation -= extra;
                    hlCompensation = Math.max(hlCompensation, hlCompMin);
                    console.log(
                        `[LUT-自动高光压缩] bright=${params.bright} > ` +
                        `${CONFIG.autoHlCompBrightThreshold}，` +
                        `highlight 额外压 ${extra} 档，` +
                        `hlCompensation=${hlCompensation}`
                    );
                }
            }
        }

        // ---- 高光搜索 ----
        {
            const baseDetected = statsOfCoarse(params);
            const currentLum = baseDetected.skin_lum;
            let [hlLo, hlHi] = getDynamicHlTarget(currentLum);

            if (isHighHlScene) {
                hlHi = Math.min(hlHi, HH.hlHiCap);
                console.log(
                    `[LUT-高光搜索] 高光过量场景，hlHi 收紧到 ${hlHi.toFixed(3)}`
                );
            }

            const hlBase = template.highlight + hlCompensation;
            let bestHl = params.highlight, bestErr = Infinity;
            const hMin = isHighHlScene ? -16 : adjustRange.highlight[0];
            const hMax = adjustRange.highlight[1];
            // ★ 中央 50% + 高命中下，highlight 下限从 -10 收紧到 -5
            const hlHardMin = isHighHlScene
                ? HH.highlightHardMin
                : (isCenterFallback && useHighMatch ? -5 : -10);

            for (let delta = hMin; delta <= hMax; delta += 1) {
                const hl = hlBase + delta;
                const p = Object.assign({}, params, { highlight: hl });
                const detected = statsOfCoarse(p);
                let err = Math.pow(rangeError(detected.full_hl_ratio, [hlLo, hlHi]), 2);
                err += 10 * Math.pow(rangeError(detected.skin_rb, targetsEff.skin_rb), 2);
                if (err < bestErr) { bestErr = err; bestHl = hl; }
            }
            params.highlight = Math.max(bestHl, hlHardMin);
        }

        // ---- 阴影搜索 ----
        {
            const DARK_LUM_THRESHOLD = 0.20;
            const DARK_LUM_TARGET = 0.30;

            const pBase = Object.assign({}, params, { shadow: 0 });
            const baseTransformed = applyParamsToSkinArray(coarse, pBase);
            const nPix = baseTransformed.length / 3;
            const darkMask = new Uint8Array(nPix);
            let nDark = 0;
            for (let i = 0; i < nPix; i++) {
                const lum = KR * baseTransformed[i * 3] +
                    KG * baseTransformed[i * 3 + 1] +
                    KB * baseTransformed[i * 3 + 2];
                if (lum < DARK_LUM_THRESHOLD) { darkMask[i] = 1; nDark++; }
            }

            if (nDark > 0) {
                let bestSh = params.shadow, bestErr = Infinity;
                const sMin = template.shadow + adjustRange.shadow[0];
                const sMax = isHighHlScene
                    ? template.shadow + Math.round(adjustRange.shadow[1] * HH.shadowMaxFactor)
                    : template.shadow + adjustRange.shadow[1];
                for (let sh = sMin; sh <= sMax; sh += 1) {
                    const p = Object.assign({}, params, { shadow: sh });
                    const transformed = applyParamsToSkinArray(coarse, p);
                    const darkLums = [];
                    for (let i = 0; i < nPix; i++) {
                        if (darkMask[i]) {
                            darkLums.push(
                                KR * transformed[i * 3] +
                                KG * transformed[i * 3 + 1] +
                                KB * transformed[i * 3 + 2]
                            );
                        }
                    }
                    darkLums.sort((a, b) => a - b);
                    const medDark = darkLums[Math.floor(darkLums.length / 2)];
                    let err = Math.pow(medDark - DARK_LUM_TARGET, 2);
                    const detected = sampleSkinStats(transformed);
                    err += 1 * Math.pow(rangeError(detected.skin_rb, targetsEff.skin_rb), 2);
                    if (err < bestErr) { bestErr = err; bestSh = sh; }
                }
                params.shadow = bestSh;
            }
        }

        // ---- 最终亮度保护（原有） ----
        {
            const finalDetected = statsOfCoarse(params);
            const finalLum = finalDetected.skin_lum;
            const [lumLo, lumHi] = targetsEff.skin_lum;
            let lumTarget = lumLo + (lumHi - lumLo) * 0.8;

            const rbHi = targetsEff.skin_rb[1];
            const rbTrigger = rbHi * 1.2;
            let lumTargetHigh;
            if (info.skin_rb > rbTrigger) {
                lumTargetHigh = lumLo + (lumHi - lumLo) * 0.9;
            } else {
                lumTargetHigh = lumTarget;
            }

            const LUM_HARD_FLOOR = useExtreme ? 0.0 : 0.44;

            const origLumInRange = (origLum !== undefined &&
                lumLo <= origLum && origLum <= lumHi);
            const origBelowFloor = (origLum !== undefined && origLum < LUM_HARD_FLOOR);

            let needRecheck = false;
            if (origLumInRange && finalLum < origLum - 0.005) {
                needRecheck = true;
            } else if (origBelowFloor && finalLum < LUM_HARD_FLOOR) {
                needRecheck = true;
            } else if (finalLum < lumTargetHigh * 0.95) {
                needRecheck = true;
            } else if (finalLum > lumHi * 1.05) {
                needRecheck = true;
            }

            if (needRecheck) {
                let recheckTarget;
                if (origLumInRange) {
                    recheckTarget = origBelowFloor
                        ? Math.max(origLum, LUM_HARD_FLOOR) : origLum;
                } else if (origBelowFloor) {
                    recheckTarget = Math.max(LUM_HARD_FLOOR, lumTargetHigh);
                } else {
                    recheckTarget = lumTargetHigh;
                }

                let bestBright3 = params.bright, bestErr3 = Infinity;
                const baseBright3 = params.bright;
                const lo3 = Math.max(adjustRange.bright[0],
                    baseBright3 - 6 - template.bright);
                const hi3 = Math.min(adjustRange.bright[1],
                    baseBright3 + 15 - template.bright);
                for (let delta = lo3; delta <= hi3; delta += 1) {
                    const bright = template.bright + delta;
                    const p = Object.assign({}, params, { bright });
                    const detected = statsOfCoarse(p);
                    let lumErr;
                    if (detected.skin_lum < recheckTarget) {
                        lumErr = Math.pow(recheckTarget - detected.skin_lum, 2);
                    } else if (detected.skin_lum > lumHi) {
                        lumErr = Math.pow(detected.skin_lum - recheckTarget, 2);
                    } else {
                        lumErr = 0;
                    }
                    let err = lumErr;
                    err += 0.5 * Math.pow(rangeError(detected.skin_rb, targetsEff.skin_rb), 2);
                    err += hlWeightEff * Math.pow(rangeError(detected.full_hl_ratio, targetsEff.full_hl_ratio), 2);
                    if (err < bestErr3) { bestErr3 = err; bestBright3 = bright; }
                }
                params.bright = bestBright3;
            }
        }

        // ---- 最终亮度保护（用全样本，限制补偿上限） ----
        {
            if (origLum !== undefined && origLum !== null) {
                const target = origLum - CONFIG.lumGuardTolerance;
                const BRIGHT_GUARD_MAX_ADD = CONFIG.brightGuardMaxAdd;
                const HIGHLIGHT_GUARD_MAX_ADD = CONFIG.highlightGuardMaxAdd;

                let guard = 0;
                while (guard < BRIGHT_GUARD_MAX_ADD) {
                    const detected = sampleSkinStats(
                        applyParamsToSkinArray(skinPixels, params)
                    );
                    if (detected.skin_lum >= target) break;
                    const nextBright = params.bright + 1;
                    const bMax = template.bright + adjustRange.bright[1];
                    if (nextBright > bMax) break;
                    params.bright = nextBright;
                    guard++;
                }

                let guard2 = 0;
                while (guard2 < HIGHLIGHT_GUARD_MAX_ADD) {
                    const detected = sampleSkinStats(
                        applyParamsToSkinArray(skinPixels, params)
                    );
                    if (detected.skin_lum >= target) break;
                    const nextHl = params.highlight + 1;
                    const hlMax = template.highlight + adjustRange.highlight[1];
                    if (nextHl > hlMax) break;
                    params.highlight = nextHl;
                    guard2++;
                }
            }
        }

        // ---- 缺亮补亮 ----
        {
            if (needForceBright) {
                const bMax = template.bright + adjustRange.bright[1];
                const ratio = Math.max(0, Math.min(1,
                    highPixels / CONFIG.highPixelLowThreshold));
                const dynamicTarget = Math.round(
                    CONFIG.highPixelForceBright * (1 - ratio)
                    + params.bright * ratio
                );
                const targetBright = Math.min(dynamicTarget, bMax);

                if (params.bright < targetBright) {
                    console.log(
                        `[LUT-缺亮补亮] high_pixels=${highPixels} ` +
                        `(ratio=${ratio.toFixed(2)}), ` +
                        `bright ${params.bright} → ${targetBright} ` +
                        `(动态目标: ${CONFIG.highPixelForceBright}×` +
                        `${(1-ratio).toFixed(2)} + ${params.bright}×${ratio.toFixed(2)})`
                    );
                    params.bright = targetBright;
                } else {
                    console.log(
                        `[LUT-缺亮补亮] high_pixels=${highPixels} 低，` +
                        `但 bright=${params.bright} 已 >= 动态目标 ${targetBright}，不调整`
                    );
                }
            }
        }

        // ---- 亮度钳制（增加 high_pixels > 300 条件） ----
        if (!useExtreme && !useHighMatch && highPixels > 300) {
            const absTemp = Math.abs(params.temp);
            const brightRaise = Math.max(0, params.bright - template.bright);
            const maxBrightRaise = Math.round(absTemp * 1.2);
            if (brightRaise > maxBrightRaise) {
                console.log(
                    `[LUT-亮度钳制] 标准模板：high_pixels=${highPixels} > 300，` +
                    `|temp| = ${absTemp}，` +
                    `亮度提高上限 = ${absTemp} × 1.2 = ${maxBrightRaise}，` +
                    `当前亮度提高 ${brightRaise} → 钳制到 ${maxBrightRaise}`
                );
                params.bright = template.bright + maxBrightRaise;
            } else {
                console.log(
                    `[LUT-亮度钳制] high_pixels=${highPixels} > 300，` +
                    `|temp| = ${absTemp}，` +
                    `亮度提高 ${brightRaise} ≤ 上限 ${maxBrightRaise}，不钳制`
                );
            }
        } else if (!useExtreme && !useHighMatch) {
            console.log(
                `[LUT-亮度钳制] 标准模板，但 high_pixels=${highPixels} ≤ 300，跳过亮度钳制`
            );
        }

        // ---- 参数力度协调器 ----
        const balanced = balanceParamStrength(params, template, adjustRange, isHighHlScene);

        // ★ 人脸框模式 + 高光像素多 + 高光 R/B 高：
        //    亮度提高量 ×0.7；若 highlight 为正值（加高光），强制为 -1
        if (!useExtreme && !useHighMatch && info.regionName === '人脸框') {
            const hp = info.high_pixels !== undefined ? info.high_pixels : 0;
            const hrb = info.high_rb !== undefined ? info.high_rb : 0;
            if (hp > 500 && hrb > 0.95) {
                const brightRaise = balanced.bright - template.bright;
                if (brightRaise > 0) {
                    const reduced = Math.round(brightRaise * 0.7);
                    console.log(
                        `[LUT-人脸框高光钳制] high_pixels=${hp} > 500 且 ` +
                        `high_rb=${hrb.toFixed(3)} > 0.95 → ` +
                        `亮度提高量 ${brightRaise} → ${reduced}（×0.7）`
                    );
                    balanced.bright = template.bright + reduced;
                } else {
                    console.log(
                        `[LUT-人脸框高光钳制] high_pixels=${hp} > 500 且 ` +
                        `high_rb=${hrb.toFixed(3)} > 0.95，` +
                        `但亮度提高量 ${brightRaise} ≤ 0，亮度不调整`
                    );
                }

                if (balanced.highlight > 0) {
                    console.log(
                        `[LUT-人脸框高光钳制] highlight=${balanced.highlight} > 0 → 强制为 -1`
                    );
                    balanced.highlight = -1;
                } else {
                    console.log(
                        `[LUT-人脸框高光钳制] highlight=${balanced.highlight} ≤ 0，保持原样`
                    );
                }
            }
        }

        // ★ 人脸框 + 高命中模板：若 highlight 为减值，强制为 -2
        if (!useExtreme && useHighMatch && info.regionName === '人脸框') {
            if (balanced.highlight < 0) {
                console.log(
                    `[LUT-人脸框高命中钳制] highlight=${balanced.highlight} < 0 → 强制为 -2`
                );
                balanced.highlight = -2;
            } else {
                console.log(
                    `[LUT-人脸框高命中钳制] highlight=${balanced.highlight} ≥ 0，保持原样`
                );
            }
        }

        // ★ 亮度 >= 8：色温强制为 -8；同时对比度、饱和度也跟上
        {
            if (balanced.bright >= 8) {
                if (balanced.temp > -15) {
                    console.log(
                        `[LUT-亮度色温联动] bright=${balanced.bright} >= 8 且 ` +
                        `temp=${balanced.temp} > -15 → 强制为 -8`
                    );
                    balanced.temp = -8;
                } else {
                    console.log(
                        `[LUT-亮度色温联动] bright=${balanced.bright} >= 8，` +
                        `但 temp=${balanced.temp} ≤ -15，保持原样`
                    );
                }
                // ★ 亮度提高时，往冷白方向调：sat 和 contrast 往负走
                if (balanced.contrast > -1) {
                    console.log(
                        `[LUT-亮度联动] bright=${balanced.bright} >= 8 → contrast ${balanced.contrast} → -1`
                    );
                    balanced.contrast = -1;
                }
                if (balanced.sat > -1) {
                    console.log(
                        `[LUT-亮度联动] bright=${balanced.bright} >= 8 → sat ${balanced.sat} → -1`
                    );
                    balanced.sat = -1;
                }
            } else {
                console.log(
                    `[LUT-亮度色温联动] bright=${balanced.bright} < 8，色温保持 ${balanced.temp}`
                );
            }
        }

        return balanced;
    }

    // ============================================================
    // 三、LUT 烘焙
    // ============================================================
    function generateLutData(size, params) {
        const data = new Float32Array(size * size * size * 3);
        let idx = 0;
        for (let b = 0; b < size; b++) {
            for (let g = 0; g < size; g++) {
                for (let r = 0; r < size; r++) {
                    let rIn = r / (size - 1);
                    let gIn = g / (size - 1);
                    let bIn = b / (size - 1);

                    let t = applyBrightnessScalar(rIn, gIn, bIn, params.bright);
                    rIn = t[0]; gIn = t[1]; bIn = t[2];
                    t = applyHighlightShadowScalar(rIn, gIn, bIn, params.highlight, params.shadow);
                    rIn = t[0]; gIn = t[1]; bIn = t[2];
                    t = applyTempTintScalar(rIn, gIn, bIn, params.temp, params.tint);
                    rIn = t[0]; gIn = t[1]; bIn = t[2];
                    t = applySaturationScalar(rIn, gIn, bIn, params.sat);
                    rIn = t[0]; gIn = t[1]; bIn = t[2];
                    t = applyContrastScalar(rIn, gIn, bIn, params.contrast);
                    rIn = t[0]; gIn = t[1]; bIn = t[2];

                    const lum = KR * rIn + KG * gIn + KB * bIn;
                    if (lum > OVEREXPOSE_LIMIT && !isNearWhite(rIn, gIn, bIn)) {
                        const s = OVEREXPOSE_LIMIT / lum;
                        rIn *= s; gIn *= s; bIn *= s;
                    }

                    data[idx++] = Math.min(1, Math.max(0, rIn));
                    data[idx++] = Math.min(1, Math.max(0, gIn));
                    data[idx++] = Math.min(1, Math.max(0, bIn));
                }
            }
        }
        return data;
    }

    // ============================================================
    function computeGlobalHlRatio(data, step) {
        const nPix = data.length / 4;
        let hl = 0, cnt = 0;
        const s = step || 1;
        for (let i = 0; i < data.length; i += 4 * s) {
            const r = data[i] / 255, g = data[i + 1] / 255, b = data[i + 2] / 255;
            const lum = KR * r + KG * g + KB * b;
            if (lum > HL_THRESHOLD) hl++;
            cnt++;
        }
        if (cnt === 0) return 0;
        return hl / cnt;
    }

    function computeRegionCrRange(data, aw, ah, region, coverage, fallback) {
        const x1 = region.x1, y1 = region.y1, x2 = region.x2, y2 = region.y2;
        const totalPix = (x2 - x1) * (y2 - y1);
        if (totalPix <= 0) return { range: fallback };

        const step = Math.max(1, Math.floor(Math.sqrt(totalPix / 40000)));

        const crList = [];
        for (let y = y1; y < y2; y += step) {
            for (let x = x1; x < x2; x += step) {
                const i = (y * aw + x) * 4;
                const r = data[i], g = data[i + 1], b = data[i + 2];
                const cr = 128 + 0.5 * r - 0.418688 * g - 0.081312 * b;
                crList.push(cr);
            }
        }
        if (crList.length === 0) return { range: fallback };

        crList.sort((a, b) => a - b);
        const total = crList.length;
        const lowIdx = Math.floor(total * (1 - coverage) / 2);
        const highIdx = Math.min(total - 1, Math.floor(total * (1 + coverage) / 2));

        let crMin = Math.floor(crList[lowIdx]);
        let crMax = Math.ceil(crList[highIdx]);

        crMin = Math.max(120, crMin);
        crMax = Math.min(190, crMax);

        if (crMax - crMin < 30) {
            return { range: fallback };
        }

        return { range: [crMin, crMax] };
    }

    function isSkinColorRegion(r, g, b, crRange, cbRange) {
        if (r < g - 4) return false;
        if (g < b - 4) return false;
        if (r <= b) return false;

        const rb = r / Math.max(b, 1);
        if (rb > 2.0) return false;

        const rg = r / Math.max(g, 1);
        if (rg > 1.6) return false;

        const cb = 128 - 0.168736 * r - 0.331264 * g + 0.5 * b;
        const cr = 128 + 0.5 * r - 0.418688 * g - 0.081312 * b;

        if (cb < cbRange[0] || cb > cbRange[1]) return false;
        if (cr < crRange[0] || cr > crRange[1]) return false;

        const y = 0.299 * r + 0.587 * g + 0.114 * b;
        if (y < 30 || y > 250) return false;

        const max = Math.max(r, g, b), min = Math.min(r, g, b);
        if (max < 20) return false;
        const sat = (max - min) / max;
        if (sat < 0.08 || sat > 0.55) return false;

        return true;
    }


function sampleRgba(data, aw, ah, faceBox = null) {
 const faceDetected = !!faceBox;
 const full_hl_ratio_global = computeGlobalHlRatio(data, CONFIG.globalHlSampleStep);
        let sampleRegion;
        let regionName;
        if (faceDetected && faceBox) {
            sampleRegion = faceBox;
            regionName = '人脸框';
        } else {
            const cw = Math.floor(aw * 0.5);
            const ch = Math.floor(ah * 0.5);
            const cx0 = Math.floor((aw - cw) / 2);
            const cy0 = Math.floor((ah - ch) / 2);
            sampleRegion = { x1: cx0, y1: cy0, x2: cx0 + cw, y2: cy0 + ch };
            regionName = '中央50%';
        }

        console.log(
            `[LUT-路由] 人脸检测=${faceDetected ? '有' : '无'} → 采样区域【${regionName}】 ` +
            `[${sampleRegion.x1},${sampleRegion.y1} ${sampleRegion.x2},${sampleRegion.y2}]`
        );

        const isCenterFallback = !faceDetected;
        const FACE_SKIN_LUM_MAX_EFF = isCenterFallback
            ? 0.72
            : FACE_SKIN_LUM_MAX;
        console.log(
            `[LUT-采样] 采样区域=${isCenterFallback ? '中央50%' : '人脸框'}，` +
            `FACE_SKIN_LUM_MAX=${FACE_SKIN_LUM_MAX_EFF}`
        );

        const crInfo = computeRegionCrRange(
            data, aw, ah, sampleRegion,
            CONFIG.skinDetection.crCoverage,
            CONFIG.skinDetection.fallbackCrRange
        );
        const crRange = crInfo.range;
        const cbRange = CONFIG.skinDetection.staticCbRange;

        const temp = [];
        const highTemp = [];
        const rx1 = sampleRegion.x1, ry1 = sampleRegion.y1;
        const rx2 = sampleRegion.x2, ry2 = sampleRegion.y2;

        for (let y = ry1; y < ry2; y++) {
            for (let x = rx1; x < rx2; x++) {
                const i = (y * aw + x) * 4;
                const r = data[i], g = data[i + 1], b = data[i + 2];

                const lum = KR * r / 255 + KG * g / 255 + KB * b / 255;
                if (lum > CONFIG.highLumThreshold) {
                    highTemp.push(r / 255, g / 255, b / 255);
                }

                if (lum >= FACE_SKIN_LUM_MAX_EFF) continue;
                if (!isSkinColorRegion(r, g, b, crRange, cbRange)) continue;
                temp.push(r / 255, g / 255, b / 255);
            }
        }

        console.log(
            `[LUT-区域肤色] Cr范围=[${crRange[0]},${crRange[1]}] ` +
            `区域内肤色像素=${temp.length / 3}`
        );

        if (temp.length / 3 < CONFIG.faceBoxMinSkinPixels) {
            console.log(
                `[LUT-区域肤色] 采样区域内肤色像素不足: ${temp.length / 3} ` +
                `< ${CONFIG.faceBoxMinSkinPixels}，放弃本次`
            );
            return { pixels: null, box: faceBox, high_rb: 1.0, high_pixels: 0, full_hl_ratio_global, regionName };
        }

        let highRB = 1.0;
        const highPixels = highTemp.length / 3;
        if (highPixels >= CONFIG.highPixelMinCount) {
            const highArr = new Float32Array(highTemp);
            const highStats = sampleSkinStats(highArr);
            if (highStats) highRB = highStats.skin_rb;
        }

        console.log(
            `[LUT-路由] 采用【${regionName}】结果，N = ${temp.length / 3}`
        );

        return {
            pixels: new Float32Array(temp),
            box: faceBox,
            high_rb: highRB,
            high_pixels: highPixels,
            full_hl_ratio_global,
            regionName,
        };
    }


module.exports = { CONFIG, generateLutData, sampleSkinStats, predictParams, isExtremeDark, isHighMatch, subsample, sampleRgba };
